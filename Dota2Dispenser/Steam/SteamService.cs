using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKitDota2;

namespace Dota2Dispenser.Steam;

public class SteamService
{
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public SteamClient Client { get; }
    public SteamDota Dota { get; }

    private readonly SteamUser _user;
    private readonly SteamFriends _friends;

    private readonly CallbackManager _callbackManager;

    // наверное, это единственный раз, когда я юзал этот синтаксис
    private readonly string _username, _password;

    private readonly TimeSpan _reconnectTime = TimeSpan.FromSeconds(10);
    private readonly bool _dontStart;

    /// <summary>
    /// Это клуб дружбы, тут мы храним наших друзей.
    /// </summary>
    private readonly List<SteamID> _club = [];

    private bool _isRunning = false;

    /// <summary>
    /// Сессия начинается со Start, заканчивается на Stop.
    /// </summary>
    private object? _sessionObject = null;

    public bool LoggedIn { get; private set; } = false;

    public event Action<SteamDota.DotaPersonaStateCallback>? DotaPersonaReceived;

    public SteamService(IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory, IOptions<AppOptions> options)
    {
        this._lifetime = lifetime;
        _logger = loggerFactory.CreateLogger(this.GetType());

        _username = options.Value.SteamUsername;
        _password = options.Value.SteamPassword;
        _dontStart = options.Value.DontStartSteamClient == true;

        Client = new SteamClient();
        _callbackManager = new CallbackManager(Client);

        var gameCoordinator = Client.GetHandler<SteamGameCoordinator>()!;

        Dota = new SteamDota(Client, _callbackManager, true, loggerFactory);
        Client.AddHandler(Dota);

        _user = Client.GetHandler<SteamUser>()!;
        _friends = Client.GetHandler<SteamFriends>()!;

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

        _callbackManager.Subscribe<SteamFriends.FriendAddedCallback>(OnFriendsAdded);
        _callbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);

        _callbackManager.Subscribe<SteamDota.DotaReadyCallback>(OnDotaReady);
        _callbackManager.Subscribe<SteamDota.DotaNotReadyCallback>(OnDotaNotReady);
        _callbackManager.Subscribe<SteamDota.DotaHelloTimeoutCallback>(OnDotaTimeout);

        _callbackManager.Subscribe<SteamDota.DotaPersonaStateCallback>(OnDotaPersonaState);

        _lifetime.ApplicationStopping.Register(ApplicationStopping);
    }

    public void Init()
    {
        if (_isRunning || _dontStart)
            return;

        _isRunning = true;

        var thatObject = _sessionObject = new();

        _logger.LogInformation("Запускаем стим клиент...");

        Task.Run(async () =>
        {
            TryConnect();

            // Без sessionObject
            // Если слишком быстро сделать Stop Start, в теории можно запустить два цикла обработки колбеков
            while (_isRunning && thatObject == _sessionObject && !_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                // in order for the callbacks to get routed, they need to be handled by the manager
                // иногда по каким то причинам колбеки просто дохнут. сурс стимкита нюхать мне впадлу, я просто понадеюсь, что причина где то тут
                try
                {
                    await _callbackManager.RunWaitCallbackAsync(_lifetime.ApplicationStopping);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Колбеки реально ломаются");
                }
            }
        });
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;

        _sessionObject = null;

        _logger.LogInformation("Останавливает стим клиент...");

        Client.Disconnect();
    }

    public bool IsFriend(SteamID steamId)
    {
        lock (_club)
        {
            return _club.Contains(steamId);
        }
    }

    private void TryConnect()
    {
        _logger.LogInformation("Стим клиент пытается подключиться...");

        Client.Connect();
    }

    private void OnConnected(SteamClient.ConnectedCallback obj)
    {
        _logger.LogInformation("Клиент стима подключился, выполняется логин...");

        _user.LogOn(new SteamUser.LogOnDetails
        {
            Username = _username,
            Password = _password,
        });
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback obj)
    {
        _logger.LogInformation("Клиент стима потерял соединение. {UserInitiated}", obj.UserInitiated);

        LoggedIn = false;

        if (_isRunning)
        {
            Task.Run(async () =>
            {
                await Task.Delay(_reconnectTime);

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
        // TODO разобраться как удаление друга приходит.
        _logger.LogInformation("Добавлен друг {name} ({id}) {result}", obj.PersonaName, obj.SteamID, obj.Result);

        lock (_club)
        {
            _club.Add(obj.SteamID);
        }
    }

    private void OnFriendsList(SteamFriends.FriendsListCallback obj)
    {
        foreach (var friend in obj.FriendList.Where(f => f.Relationship == EFriendRelationship.RequestRecipient))
        {
            _logger.LogInformation("Добавляем друга {id}...", friend.SteamID);

            _friends.AddFriend(friend.SteamID);
        }

        lock (_club)
        {
            // я делаю наугад, я не знаю, что тут происходит.
            if (obj.Incremental)
            {
                foreach (var friend in obj.FriendList.Where(f => f.Relationship != EFriendRelationship.Friend))
                {
                    _club.Remove(friend.SteamID);
                }

                foreach (var friend in obj.FriendList.Where(f => f.Relationship == EFriendRelationship.Friend))
                {
                    if (_club.Contains(friend.SteamID))
                        continue;

                    _club.Add(friend.SteamID);
                }
            }
            else
            {
                _club.Clear();

                _club.AddRange(obj.FriendList
                    .Where(f => f.Relationship == EFriendRelationship.Friend)
                    .Select(f => f.SteamID));
            }
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

        Client.Disconnect();
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