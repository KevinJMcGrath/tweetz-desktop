﻿// Copyright (c) 2013 Blue Onion Software - All rights reserved

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using tweetz5.Utilities;
using tweetz5.Utilities.Translate;

// ReSharper disable InconsistentNaming

namespace tweetz5.Model
{
    public interface ITimelines : INotifyPropertyChanged
    {
        void HomeTimeline();
        void MentionsTimeline();
        void DirectMessagesTimeline();
        void FavoritesTimeline();
        void UpdateTimeStamps();
        void UpdateStatus(string[] timelines, Status[] statuses, string tweetType);
        void SwitchTimeline(string timelineName);
        void ClearAllTimelines();
        void AddFavorite(Tweet tweet);
        void RemoveFavorite(Tweet tweet);
        void Search(string query);
        void DeleteTweet(Tweet tweet);
        void Retweet(Tweet tweet);
        string TimelineName { get; set; }
        CancellationToken CancellationToken { get; }
        void SignalCancel();
        ReadSafeList<string> ScreenNames { get; }
    }

    public class Timelines : ITimelines, IDisposable
    {
        private bool _disposed;
        private string _timelineName;
        private ObservableCollection<Tweet> _timeline;
        private readonly Dictionary<string, Timeline> _timelineMap;
        private readonly Collection<Tweet> _tweets = new Collection<Tweet>();
        private CancellationTokenSource _cancellationTokenSource;
        public ReadSafeList<string> ScreenNames { get; private set; }

        private Timeline _unified
        {
            get { return _timelineMap[UnifiedName]; }
        }

        private Timeline _home
        {
            get { return _timelineMap[HomeName]; }
        }

        private Timeline _mentions
        {
            get { return _timelineMap[MentionsName]; }
        }

        private Timeline _directMessages
        {
            get { return _timelineMap[MessagesName]; }
        }

        private Timeline _favorites
        {
            get { return _timelineMap[FavoritesName]; }
        }

        private Timeline _search
        {
            get { return _timelineMap[SearchName]; }
        }

        private Visibility _searchVisibility = Visibility.Collapsed;

        public const string UnifiedName = "unified";
        public const string HomeName = "home";
        public const string MentionsName = "mentions";
        public const string MessagesName = "messages";
        public const string FavoritesName = "favorites";
        public const string SearchName = "search";

        public Action<Action> DispatchInvokerOverride { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public Timelines()
        {
            _timelineMap = new Dictionary<string, Timeline>
            {
                {UnifiedName, new Timeline()},
                {HomeName, new Timeline()},
                {MentionsName, new Timeline()},
                {MessagesName, new Timeline()},
                {FavoritesName, new Timeline()},
                {SearchName, new Timeline()}
            };

            _cancellationTokenSource = new CancellationTokenSource();
            ScreenNames = new ReadSafeList<string>();
        }

        public ObservableCollection<Tweet> Timeline
        {
            get { return _timeline; }
            set
            {
                if (_timeline != value)
                {
                    _timeline = value;
                    OnPropertyChanged();
                }
            }
        }

        public string TimelineName
        {
            get { return _timelineName; }
            set
            {
                if (_timelineName != value)
                {
                    _timelineName = value;
                    OnPropertyChanged();
                }
            }
        }

        public Visibility SearchVisibility
        {
            get { return _searchVisibility; }
            set
            {
                if (_searchVisibility != value)
                {
                    _searchVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public void ClearAllTimelines()
        {
            foreach (var timeline in _timelineMap)
            {
                timeline.Value.Clear();
            }
        }

        private bool UpdateTimelines(Timeline[] timelines, IEnumerable<Status> statuses, string tweetType)
        {
            var updated = false;
            foreach (var status in statuses)
            {
                var tweet = CreateTweet(tweetType, status);
                if (tweetType != "s")
                {
                    var index = _tweets.IndexOf(tweet);
                    if (index == -1)
                        _tweets.Add(tweet);
                    else
                        tweet = _tweets[index];

                    if (tweet.TweetType.Contains(tweetType) == false)
                        tweet.TweetType += tweetType;
                }

                foreach (var timeline in timelines.Where(timeline => timeline.Tweets.IndexOf(tweet) == -1))
                {
                    timeline.Tweets.Add(tweet);
                    updated = true;
                }

                if (ScreenNames.Contains(tweet.ScreenName) == false)
                {
                    ScreenNames.Add(tweet.ScreenName);
                }
                if (status.Entities.Mentions != null)
                {
                    ScreenNames.AddRange(
                        status.Entities.Mentions
                            .Where(m => ScreenNames.Contains(m.ScreenName, StringComparer.CurrentCultureIgnoreCase) == false)
                            .Select(m => m.ScreenName));
                }
            }

            if (updated)
            {
                foreach (var timeline in timelines)
                {
                    SortTweetCollection(timeline.Tweets);
                }
            }

            return updated;
        }

        public static Tweet CreateTweet(string tweetType, Status status)
        {
            var createdAt = DateTime.ParseExact(status.CreatedAt, "ddd MMM dd HH:mm:ss zzz yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

            var displayStatus = status.RetweetedStatus ?? status;

            // Direct messages don't have a User. Instead, dm's use sender and recipient collections.
            if (displayStatus.User == null)
            {
                var screenName = new OAuth().ScreenName;
                displayStatus.User = (status.Recipient.ScreenName == screenName) ? status.Sender : status.Recipient;
            }

            var tweet = new Tweet
            {
                StatusId = status.Id,
                Name = displayStatus.User.Name,
                ScreenName = displayStatus.User.ScreenName,
                ProfileImageUrl = displayStatus.User.ProfileImageUrl,
                Text = displayStatus.Text,
                MarkupNodes = BuildMarkupNodes(displayStatus.Text, displayStatus.Entities),
                CreatedAt = createdAt,
                TimeAgo = TimeAgo(createdAt),
                TweetType = tweetType,
                Favorited = status.Favorited,
                IsRetweet = status.Retweeted,
                RetweetedBy = RetweetedBy(status),
                RetweetStatusId = (status.RetweetedStatus != null) ? status.RetweetedStatus.Id : string.Empty,
                MediaLinks = status.Entities.Media != null ? status.Entities.Media.Select(m => m.MediaUrl).ToArray() : new string[0]
            };

            return tweet;
        }

        private static void PlayNotification()
        {
            if (Application.Current != null)
            {
                Commands.ChirpCommand.Command.Execute(string.Empty, Application.Current.MainWindow);
            }
        }

        public static string RetweetedBy(Status status)
        {
            if (status.RetweetedStatus != null)
            {
                var oauth = new OAuth();
                return oauth.ScreenName != status.User.ScreenName ? status.User.Name : string.Empty;
            }
            return string.Empty;
        }

        internal class MarkupItem
        {
            public string NodeType { get; set; }
            public string Text { get; set; }
            public int Start { get; set; }
            public int End { get; set; }
        }

        public static MarkupNode[] BuildMarkupNodes(string text, Entities entities)
        {
            var markupItems = new List<MarkupItem>();

            if (entities.Urls != null)
            {
                markupItems.AddRange(entities.Urls.Select(url => new MarkupItem
                {
                    NodeType = "url",
                    Text = url.Url,
                    Start = url.Indices[0],
                    End = url.Indices[1]
                }));
            }

            if (entities.Mentions != null)
            {
                markupItems.AddRange(entities.Mentions.Select(mention => new MarkupItem
                {
                    NodeType = "mention",
                    Text = mention.ScreenName,
                    Start = mention.Indices[0],
                    End = mention.Indices[1]
                }));
            }

            if (entities.HashTags != null)
            {
                markupItems.AddRange(entities.HashTags.Select(hashtag => new MarkupItem
                {
                    NodeType = "hashtag",
                    Text = hashtag.Text,
                    Start = hashtag.Indices[0],
                    End = hashtag.Indices[1]
                }));
            }

            if (entities.Media != null)
            {
                markupItems.AddRange(entities.Media.Select(media => new MarkupItem
                {
                    NodeType = "media",
                    Text = media.Url,
                    Start = media.Indices[0],
                    End = media.Indices[1]
                }));
            }

            var start = 0;
            var nodes = new List<MarkupNode>();
            markupItems.Sort((l, r) => l.Start - r.Start);
            foreach (var item in markupItems)
            {
                if (item.Start >= start) nodes.Add(new MarkupNode("text", text.Substring(start, item.Start - start)));
                nodes.Add(new MarkupNode(item.NodeType, item.Text));
                start = item.End;
            }
            if (start < text.Length) nodes.Add(new MarkupNode("text", text.Substring(start)));
            return nodes.ToArray();
        }

        private static string TimeAgo(DateTime time)
        {
            var timespan = DateTime.UtcNow - time;
            if (timespan.TotalSeconds < 60) return string.Format((string) TranslationService.Instance.Translate("time_ago_seconds"), (int) timespan.TotalSeconds);
            if (timespan.TotalMinutes < 60) return string.Format((string) TranslationService.Instance.Translate("time_ago_minutes"), (int) timespan.TotalMinutes);
            if (timespan.TotalHours < 24) return string.Format((string) TranslationService.Instance.Translate("time_ago_hours"), (int) timespan.TotalHours);
            if (timespan.TotalDays < 3) return string.Format((string) TranslationService.Instance.Translate("time_ago_days"), (int) timespan.TotalDays);
            return time.ToString((string) TranslationService.Instance.Translate("time_ago_date"));
        }

        private void DispatchInvoker(Action callback)
        {
            var invoker = DispatchInvokerOverride ?? Application.Current.Dispatcher.Invoke;
            invoker(callback);
        }

        public void SwitchTimeline(string timelineName)
        {
            Timeline timeline;
            if (_timelineMap.TryGetValue(timelineName, out timeline))
            {
                Timeline = timeline.Tweets;
                TimelineName = timelineName;
                SearchVisibility = timelineName == SearchName ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RemoveStatus(Tweet tweet)
        {
            foreach (var timeline in _timelineMap.Values)
            {
                timeline.Tweets.Remove(tweet);
            }
        }

        private static ulong MaxSinceId(ulong currentSinceId, Status[] statuses)
        {
            return statuses.Length > 0 ? Math.Max(currentSinceId, statuses.Max(s => ulong.Parse(s.Id))) : currentSinceId;
        }

        public void HomeTimeline()
        {
            var twitter = new Twitter();
            var statuses = twitter.HomeTimeline(_home.SinceId);
            _home.SinceId = MaxSinceId(_home.SinceId, statuses);
            UpdateStatus(new[] {HomeName, UnifiedName}, statuses, "h");
        }

        public void UpdateStatus(string[] timelineNames, Status[] statuses, string tweetType)
        {
            DispatchInvoker(() =>
            {
                var timelines = timelineNames.Select(timeline => _timelineMap[timeline]).ToArray();
                if (UpdateTimelines(timelines, statuses, tweetType))
                {
                    if (timelineNames.Contains(HomeName)) PlayNotification();
                }
            });
        }

        public void MentionsTimeline()
        {
            var twitter = new Twitter();
            var statuses = twitter.MentionsTimeline(_mentions.SinceId);
            _mentions.SinceId = MaxSinceId(_mentions.SinceId, statuses);
            DispatchInvoker(() =>
            {
                if (UpdateTimelines(new[] {_mentions, _unified}, statuses, "m")) PlayNotification();
                foreach (var tweet in _unified.Tweets.Where(h => statuses.Any(s => s.Id == h.StatusId)))
                {
                    tweet.TweetType += "m";
                }
            });
        }

        public void FavoritesTimeline()
        {
            var twitter = new Twitter();
            var statuses = twitter.FavoritesTimeline(_favorites.SinceId);
            _favorites.SinceId = MaxSinceId(_favorites.SinceId, statuses);
            DispatchInvoker(() =>
            {
                UpdateTimelines(new[] {_favorites}, statuses, "f");
                foreach (var tweet in _home.Tweets.Where(t => statuses.Any(s => s.Id == t.StatusId || s.Id == t.RetweetStatusId)))
                {
                    tweet.Favorited = true;
                }
            });
        }

        public void DirectMessagesTimeline()
        {
            var twitter = new Twitter();
            var statuses = twitter.DirectMessagesTimeline(_directMessages.SinceId);
            _directMessages.SinceId = MaxSinceId(_favorites.SinceId, statuses);
            DispatchInvoker(() =>
            {
                if (UpdateTimelines(new[] {_directMessages, _unified}, statuses, "d")) PlayNotification();
                foreach (var tweet in _unified.Tweets.Where(h => statuses.Any(s => s.Id == h.StatusId)))
                {
                    tweet.TweetType += "d";
                }
            });
        }

        public void UpdateTimeStamps()
        {
            DispatchInvoker(() =>
            {
                if (Timeline != null)
                {
                    foreach (var tweet in Timeline)
                    {
                        tweet.TimeAgo = TimeAgo(tweet.CreatedAt);
                    }
                }
            });
        }

        public void AddFavorite(Tweet tweet)
        {
            if (tweet.Favorited) return;
            Twitter.CreateFavorite(tweet.StatusId);
            tweet.Favorited = true;
            var index = _tweets.IndexOf(tweet);
            if (index == -1)
            {
                tweet.TweetType += "f";
                _tweets.Add(tweet);
            }
            else
            {
                _tweets[index].TweetType += "f";
                _tweets[index].Favorited = true;
            }
            if (_favorites.Tweets.Contains(tweet) == false)
            {
                _favorites.Tweets.Add(tweet);
                SortTweetCollection(_favorites.Tweets);
            }
        }

        public void RemoveFavorite(Tweet tweet)
        {
            if (tweet.Favorited == false) return;
            Twitter.DestroyFavorite(tweet.StatusId);
            var index = _tweets.IndexOf(tweet);
            var t = _tweets[index];
            t.Favorited = false;
            t.TweetType = t.TweetType.Replace("f", "");
            _favorites.Tweets.Remove(t);
        }

        private static void SortTweetCollection(ObservableCollection<Tweet> collection)
        {
            var i = 0;
            foreach (var item in collection.OrderByDescending(s => s.CreatedAt))
            {
                // Move will trigger a properychanged event even if the indexes are the same.
                var indexOfItem = collection.IndexOf(item);
                if (indexOfItem != i) collection.Move(indexOfItem, i);
                i += 1;
            }
        }

        public void Search(string query)
        {
            _search.Clear();
            Task.Run(() =>
            {
                var json = Twitter.Search(query + "+exclude:retweets");
                var statuses = SearchStatuses.ParseJson(json);
                UpdateStatus(new[] {SearchName}, statuses, "s");
            }, CancellationToken);
        }

        public void DeleteTweet(Tweet tweet)
        {
            Twitter.DestroyStatus(tweet.StatusId);
            RemoveStatus(tweet);
        }

        public void Retweet(Tweet tweet)
        {
            if (tweet.IsRetweet)
            {
                var id = string.IsNullOrWhiteSpace(tweet.RetweetStatusId) ? tweet.StatusId : tweet.RetweetStatusId;
                var json = Twitter.GetTweet(id);
                var status = Status.ParseJson("[" + json + "]")[0];
                var retweetStatusId = status.CurrentUserRetweet.Id;
                Twitter.DestroyStatus(retweetStatusId);
                tweet.IsRetweet = false;
            }
            else
            {
                Twitter.RetweetStatus(tweet.StatusId);
                tweet.IsRetweet = true;
            }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationTokenSource.Token; }
        }

        public void SignalCancel()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing == false) return;
            if (_cancellationTokenSource != null) _cancellationTokenSource.Dispose();
        }
    }
}