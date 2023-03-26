using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKitDota2;

namespace Dota2Dispenser.Steam;

public class SteamService
{
    readonly ILogger _logger;
    readonly IHostApplicationLifetime _lifetime;

    public readonly SteamClient client;
    readonly SteamUser user;
    readonly SteamFriends friends;
    public readonly SteamDota dota;

    readonly CallbackManager callbackManager;

    // наверное, это единственный раз, когда я юзал этот синтаксис
    readonly string username, password;

    readonly TimeSpan reconnectTime = TimeSpan.FromSeconds(10);

    bool isRunning = false;
    /// <summary>
    /// Сессия начинается со Start, заканчивается на Stop.
    /// </summary>
    object? sessionObject = null;

    public bool LoggedIn { get; private set; } = false;

    public event Action<SteamDota.DotaPersonaStateCallback>? DotaPersonaReceived;

    public SteamService(IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory, IOptions<AppOptions> options)
    {
        this._lifetime = lifetime;
        _logger = loggerFactory.CreateLogger(this.GetType());

        username = options.Value.SteamUsername;
        password = options.Value.SteamPassword;

        client = new SteamClient();
        callbackManager = new CallbackManager(client);

        var gameCoordinator = client.GetHandler<SteamGameCoordinator>()!;

        dota = new SteamDota(client, callbackManager, true, loggerFactory);
        client.AddHandler(dota);

        user = client.GetHandler<SteamUser>()!;
        friends = client.GetHandler<SteamFriends>()!;

        callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

        callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

        callbackManager.Subscribe<SteamFriends.FriendAddedCallback>(OnFriendsAdded);
        callbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);

        callbackManager.Subscribe<SteamDota.DotaReadyCallback>(OnDotaReady);
        callbackManager.Subscribe<SteamDota.DotaNotReadyCallback>(OnDotaNotReady);
        callbackManager.Subscribe<SteamDota.DotaHelloTimeoutCallback>(OnDotaTimeout);

        callbackManager.Subscribe<SteamDota.DotaPersonaStateCallback>(OnDotaPersonaState);

        _lifetime.ApplicationStopping.Register(ApplicationStopping);
    }

    public void Init()
    {
        if (isRunning)
            return;

        isRunning = true;

        var thatObject = sessionObject = new();

        _logger.LogInformation("Запускаем стим клиент...");

        Task.Run(() =>
        {
            TryConnect();

            // Без sessionObject
            // Если слишком быстро сделать Stop Start, в теории можно запустить два цикла обработки колбеков
            while (isRunning && thatObject == sessionObject && !_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                // in order for the callbacks to get routed, they need to be handled by the manager
                callbackManager.RunWaitCallbacks(TimeSpan.FromMilliseconds(500));
            }
        });
    }

    public void Stop()
    {
        if (!isRunning)
            return;

        isRunning = false;

        sessionObject = null;

        _logger.LogInformation("Останавливает стим клиент...");

        client.Disconnect();
    }

    private void TryConnect()
    {
        _logger.LogInformation("Стим клиент пытается подключиться...");

        client.Connect();
    }

    private void OnConnected(SteamClient.ConnectedCallback obj)
    {
        _logger.LogInformation("Клиент стима подключился, выполняется логин...");

        user.LogOn(new SteamUser.LogOnDetails
        {
            Username = username,
            Password = password,
        });
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback obj)
    {
        _logger.LogInformation("Клиент стима потерял соединение. {UserInitiated}", obj.UserInitiated);

        LoggedIn = false;

        if (isRunning)
        {
            Task.Run(async () =>
            {
                await Task.Delay(reconnectTime);

                TryConnect();
            });
        }
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback obj)
    {
        _logger.LogInformation("Логин завершён. {Result}", obj.Result);

        if (obj.Result == EResult.OK)
        {
            LoggedIn = true;
        }
        else if (obj.Result == EResult.AccountLogonDenied)
        {
            _logger.LogCritical("AccountLogonDenied");
            Stop();
        }
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback obj)
    {
        _logger.LogInformation("Разлогинились. {Result}", obj.Result);

        LoggedIn = false;
    }

    private void OnFriendsAdded(SteamFriends.FriendAddedCallback obj)
    {
        _logger.LogInformation("Добавлен друг {name} ({id}) {result}", obj.PersonaName, obj.SteamID, obj.Result);
    }

    private void OnFriendsList(SteamFriends.FriendsListCallback obj)
    {
        foreach (var friend in obj.FriendList.Where(f => f.Relationship == EFriendRelationship.RequestRecipient))
        {
            _logger.LogInformation("Добавляем друга {id}...", friend.SteamID);

            friends.AddFriend(friend.SteamID);
        }
    }

    private void OnDotaReady(SteamDota.DotaReadyCallback obj)
    {
        _logger.LogInformation("Дота готова.");
    }

    private void OnDotaNotReady(SteamDota.DotaNotReadyCallback obj)
    {
        _logger.LogInformation("Дота не готова.");
    }

    private void OnDotaTimeout(SteamDota.DotaHelloTimeoutCallback obj)
    {
        _logger.LogInformation("Дота таймаут...");

        client.Disconnect();
    }

    private void OnDotaPersonaState(SteamDota.DotaPersonaStateCallback obj)
    {
        DotaPersonaReceived?.Invoke(obj);
    }

    private void ApplicationStopping()
    {
        Stop();
    }
}
