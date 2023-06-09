﻿// <auto-generated />
using System;
using Dota2Dispenser.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Dota2Dispenser.Migrations
{
    [DbContext(typeof(DotaContext))]
    [Migration("20230530182057_Slots")]
    partial class Slots
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "7.0.5");

            modelBuilder.Entity("Dota2Dispenser.Database.Models.AccountModel", b =>
                {
                    b.Property<ulong>("SteamID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("DateAdded")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Note")
                        .HasColumnType("TEXT");

                    b.HasKey("SteamID");

                    b.HasIndex("SteamID");

                    b.ToTable("Accounts");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.MatchModel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("GameDate")
                        .HasColumnType("INTEGER");

                    b.Property<int>("MatchResult")
                        .HasColumnType("INTEGER");

                    b.Property<string>("RichPresenceLobbyType")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("WatchableGameId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("Matches");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.PlayerModel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<uint>("HeroId")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("LeaverStatus")
                        .HasColumnType("INTEGER");

                    b.Property<int>("MatchId")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("PartyIndex")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("PlayerSlot")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("SteamId")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("TeamNumber")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("TeamSlot")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("MatchId");

                    b.HasIndex("SteamId");

                    b.ToTable("Players");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.RequestModel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("AccountId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("DateAdded")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Identity")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Note")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("AccountId");

                    b.ToTable("Requests");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.MatchModel", b =>
                {
                    b.OwnsOne("Dota2Dispenser.Database.Models.DetailsMatchInfo", "DetailsInfo", b1 =>
                        {
                            b1.Property<int>("MatchModelId")
                                .HasColumnType("INTEGER");

                            b1.Property<TimeSpan>("Duration")
                                .HasColumnType("TEXT");

                            b1.Property<bool?>("RadiantWin")
                                .HasColumnType("INTEGER");

                            b1.HasKey("MatchModelId");

                            b1.ToTable("Matches");

                            b1.WithOwner()
                                .HasForeignKey("MatchModelId");
                        });

                    b.OwnsOne("Dota2Dispenser.Database.Models.SourceMatchInfo", "TvInfo", b1 =>
                        {
                            b1.Property<int>("MatchModelId")
                                .HasColumnType("INTEGER");

                            b1.Property<uint?>("AverageMmr")
                                .HasColumnType("INTEGER");

                            b1.Property<uint>("GameMode")
                                .HasColumnType("INTEGER");

                            b1.Property<uint>("LobbyType")
                                .HasColumnType("INTEGER");

                            b1.Property<ulong>("MatchId")
                                .HasColumnType("INTEGER");

                            b1.HasKey("MatchModelId");

                            b1.ToTable("Matches");

                            b1.WithOwner()
                                .HasForeignKey("MatchModelId");
                        });

                    b.Navigation("DetailsInfo");

                    b.Navigation("TvInfo");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.PlayerModel", b =>
                {
                    b.HasOne("Dota2Dispenser.Database.Models.MatchModel", "Match")
                        .WithMany("Players")
                        .HasForeignKey("MatchId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Match");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.RequestModel", b =>
                {
                    b.HasOne("Dota2Dispenser.Database.Models.AccountModel", "Account")
                        .WithMany("Requests")
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Account");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.AccountModel", b =>
                {
                    b.Navigation("Requests");
                });

            modelBuilder.Entity("Dota2Dispenser.Database.Models.MatchModel", b =>
                {
                    b.Navigation("Players");
                });
#pragma warning restore 612, 618
        }
    }
}
