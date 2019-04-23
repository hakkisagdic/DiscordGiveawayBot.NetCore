﻿using BotClient;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UltraGiveawayBot
{
    public class GiveAway : ModuleBase
    {
        #region Static

        public static List<GiveAwayValues> InitValues { get; private set; }

        static GiveAway()
        {
            InitValues = new List<GiveAwayValues>();
        }

        #endregion

        #region Members & Constructor

        private IDiscordClient _discordClient;
        private GiveAwayFunctions _functions;


        public GiveAway(IDiscordClient client)
        {
            _discordClient = client;
            _functions = new GiveAwayFunctions(client);

            _discordClient.Client.MessageReceived -= Client_MessageReceived;
            _discordClient.Client.MessageReceived += Client_MessageReceived;
        }

        #endregion

        #region Discord commands

        [Command("initgiveaway"), Summary("Initializes a new giveaway")]
        public async Task InitGiveAway(IMessageChannel channel)
        {
            StopGiveawayInternal();
            string message = _discordClient.CultureHelper.GetAdminString("GiveawayStartNew") + Environment.NewLine +
                             _discordClient.CultureHelper.GetAdminString("GiveawayTypeCancel") + Environment.NewLine +
                             _discordClient.CultureHelper.GetAdminString("GiveawayEnterTime") + Environment.NewLine +
                             _discordClient.CultureHelper.GetAdminString("GiveawayTimeExample");
            GiveAwayValues values = new GiveAwayValues();
            values.ServerGuild = Context.Guild;
            values.GiveawayChannel = channel;
            values.AdminChannel = Context.Channel;
            values.AdminUser = Context.User;
            values.State = GiveAwayState.SetGiveAwayTime;
            InitValues.Add(values);

            await ReplyAsync(message, false, null);
        }

        [Command("getwinner"), Summary("Shouts out the winners of a channel")]
        public async Task GetWinners([Summary("Name des Channels")] IMessageChannel channel, string codeword)
        {
            await ShoutOutTheWinners(channel, codeword);
        }

        [Command("cancel"), Summary("Cancels the Giveaway")]
        public async Task CancelGiveaway()
        {
            StopGiveawayInternal();
            Console.WriteLine("Giveaway stopped");

            await ReplyAsync(_discordClient.CultureHelper.GetAdminString("GiveawayStopped"));
        }

        #endregion

        #region EventHandlers

        private async Task Client_MessageReceived(Discord.WebSocket.SocketMessage arg)
        {
            GiveAwayValues inits = null;
            inits = GetCurrentInitValues();
            if (inits != null)
            {
                if (arg is SocketUserMessage userMessage &&
                    !userMessage.Author.IsBot &&
                    userMessage.Channel == inits.AdminChannel &&
                    userMessage.Author == inits.AdminUser)
                {
                    if (userMessage.Content.ToLower().Equals("!cancel"))
                    {
                        return;
                    }

                    string message = null;
                    switch (inits.State)
                    {
                        case GiveAwayState.SetGiveAwayTime:
                            message = _functions.SetGiveAwayTime(inits, userMessage.Content);
                            await ReplyAsync(message);
                            break;
                        case GiveAwayState.SetCountGiveAways:
                            message = _functions.SetCountGiveAways(inits, userMessage.Content);
                            await ReplyAsync(message);
                            break;
                        case GiveAwayState.SetCodeword:
                            message = _functions.SetCodeword(inits, userMessage.Content);
                            await ReplyAsync(message);
                            break;
                        case GiveAwayState.SetAward:
                            message = _functions.SetAward(inits, userMessage.Content);
                            await ReplyAsync(message);
                            break;
                        case GiveAwayState.Initialized:
                            await StartGiveAway(inits, userMessage.Content);
                            break;
                    }
                }
            }
        }

        #endregion

        #region Methods

        public async Task StartGiveAway(GiveAwayValues inits, string message)
        {
            if (!message.Equals("start"))
            {
                return;
            }

            _discordClient.Client.MessageReceived -= Client_MessageReceived;
            inits.Timer = new Timer();
            Console.WriteLine("StartingGiveaway");

            if (!inits.Timer.SetUpTimer(inits.GiveAwayTime, inits.GiveAwayDateTime, new Action(() =>
            {
                Task.Run(SetTimerValues);
            })))
            {
                await ReplyAsync(_discordClient.CultureHelper.GetAdminString("GiveawayErrorTimer"));
                return;
            }

            foreach (CultureInfo culture in _discordClient.CultureHelper.OutputCultures)
            {
                EmbedBuilder embed = new EmbedBuilder();
                embed.WithDescription(GetGiveawayMessage(culture, inits, true));
                AddEndTimeField(inits, embed, culture);

                await inits.GiveawayChannel.SendMessageAsync(string.Empty, false, embed.Build());
            }

            await ReplyAsync(":tada: " + _discordClient.CultureHelper.GetAdminString("GiveawayStarted"));
        }

        public string GetGiveawayMessage(CultureInfo culture, GiveAwayValues inits, bool first)
        {
            string announce = first ? _discordClient.CultureHelper.GetOutputString("GiveawayAnnounce", culture) :
                                      _discordClient.CultureHelper.GetOutputString("GiveawayAnnounceNext", culture);
            return Environment.NewLine + announce + Environment.NewLine +
                   string.Format(_discordClient.CultureHelper.GetOutputString("GiveawayAnnounceWin", culture),
                                 inits.CultureAward[culture.Name]) + Environment.NewLine +
                   string.Format(_discordClient.CultureHelper.GetOutputString("GiveawayAnnounceKeyword", culture),
                                 inits.Codeword) + " :tada:";
        }

        private void AddEndTimeField(GiveAwayValues inits, EmbedBuilder embed, CultureInfo culture)
        {
            if (inits.GiveAwayTime != null)
            {
                DateTime dt = new DateTime(1999, 1, 1, inits.GiveAwayTime.Value.Hours, inits.GiveAwayTime.Value.Minutes, 0);
                string time = dt.ToString("t", culture);

                embed.AddField(string.Format(_discordClient.CultureHelper.GetOutputString("GiveawayParticipEndTime", culture),
                               time), _discordClient.CultureHelper.GetOutputString("GoodLuck", culture));
            }
            else if (inits.GiveAwayDateTime != null)
            {
                DateTime dt = inits.GiveAwayDateTime.Value;
                string date = dt.ToString("g", culture);

                embed.AddField(string.Format(_discordClient.CultureHelper.GetOutputString("GiveawayParticipEndDate", culture),
                               date), _discordClient.CultureHelper.GetOutputString("GoodLuck", culture));
            }
        }

        private async Task SetTimerValues()
        {
            GiveAwayValues inits = GetCurrentInitValues();
            if (inits != null)
            {
                await ShoutOutTheWinners(inits.GiveawayChannel, inits.Codeword);
                inits.CountGiveAways--;
                if (inits.CountGiveAways == 0)
                {
                    StopGiveawayInternal();
                    _discordClient.Client.MessageReceived -= Client_MessageReceived;
                }
                else
                {
                    try
                    {
                        // Wait one Minute to avoid problems with timespan
                        Thread.Sleep(60000);
                        foreach (CultureInfo culture in _discordClient.CultureHelper.OutputCultures)
                        {
                            EmbedBuilder embed = new EmbedBuilder();
                            embed.WithDescription(GetGiveawayMessage(culture, inits, false));
                            AddEndTimeField(inits, embed, culture);

                            await inits.GiveawayChannel.SendMessageAsync(string.Empty, false, embed.Build());
                        }

                        inits.Timer = new Timer();
                        if (inits.GiveAwayDateTime != null)
                        {
                            inits.GiveAwayDateTime = inits.GiveAwayDateTime.Value.AddDays(1);
                        }

                        inits.Timer.SetUpTimer(inits.GiveAwayTime, inits.GiveAwayDateTime, new Action(() =>
                        {
                            Task.Run(SetTimerValues);
                        }));
                    }
                    catch (Exception ex)
                    {
                        await ReplyAsync(_discordClient.CultureHelper.GetAdminString("GiveawayErrorRepeat") + ex.Message);
                    }
                }
            }
        }

        private void StopGiveawayInternal()
        {
            GiveAwayValues inits = GetCurrentInitValues();
            if (inits != null)
            {
                inits.Reset();
                InitValues.Remove(inits);
            }
        }

        internal GiveAwayValues GetCurrentInitValues()
        {
            if (InitValues != null && Context != null)
            {
                GiveAwayValues initValues = InitValues.SingleOrDefault(x => x.ServerGuild == Context.Guild);
                return initValues;
            }
            return null;
        }

        private async Task ShoutOutTheWinners(IMessageChannel channel, string codeword)
        {
            List<IReadOnlyCollection<IMessage>> readonlymessages = await channel.GetMessagesAsync(1000).ToList();
            List<IMessage> messages = readonlymessages.SelectMany(x => x).ToList();
            IMessage latestBotMessage = messages.FirstOrDefault(x => x.Author.Id == Context.Client.CurrentUser.Id);
            List<IMessage> latestMessages;
            if (latestBotMessage != null)
            {
                latestMessages = new List<IMessage>();
                int index = messages.IndexOf(latestBotMessage);
                for (int i = 0; i < index; i++)
                {
                    latestMessages.Add(messages[i]);
                }
            }
            else
            {
                latestMessages = messages;
            }

            List<IUser> users = latestMessages.Where(m => m.Content.ToLower().Contains(codeword.ToLower()))
                                      .Select(y => y.Author).Distinct().Where(x => !x.IsBot).ToList();
            List<IUser> winners = GetWinners(1, users);

            
            foreach (CultureInfo culture in _discordClient.CultureHelper.OutputCultures)
            {
                string message = _discordClient.CultureHelper.GetOutputString("GiveawayWinner", culture) + Environment.NewLine;
                
                foreach (IUser winner in winners)
                {
                    message += $":trophy: {winner.Mention} :trophy:" + Environment.NewLine;
                }
                message += _discordClient.CultureHelper.GetOutputString("GiveawayCongrats", culture) + Environment.NewLine;

                await channel.SendMessageAsync(message);
            }

            await ReplyAsync(_discordClient.CultureHelper.GetAdminString("GiveawayWinnerAnnounced"));
        }

        public List<IUser> GetWinners(uint winnersCount, List<IUser> authors)
        {
            List<IUser> winners = new List<IUser>();
            List<int> randomNumbers = new List<int>();
            Random r = new Random();
            if (authors.Count < winnersCount)
            {
                winnersCount = (uint)authors.Count;
            }

            for (int i = 0; i < winnersCount; i++)
            {
                int nextAuthor;
                do
                {
                    nextAuthor = r.Next(authors.Count);
                } while (randomNumbers.Contains(nextAuthor));

                randomNumbers.Add(nextAuthor);
                winners.Add(authors[nextAuthor]);
            }

            return winners;
        }

        #endregion
    }
}
