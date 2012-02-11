﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MediaPortal.Configuration;
using MediaPortal.Profile;
using MediaPortal.GUI.Library;
using TraktPlugin.GUI;
using TraktPlugin.TraktHandlers;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;

namespace TraktPlugin
{
    public class TraktHelper
    {
        #region Plugin Helpers
        public static bool IsPluginEnabled(string name)
        {
            using (Settings xmlreader = new MPSettings())
            {
                return xmlreader.GetValueAsBool("plugins", name, false);
            }
        }

        public static bool IsOnlineVideosAvailableAndEnabled
        {
            get
            {
                return File.Exists(Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "OnlineVideos.MediaPortal1.dll")) && IsPluginEnabled("Online Videos");
            }
        }

        public static bool IsMovingPicturesAvailableAndEnabled
        {
            get
            {
                return File.Exists(Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "MovingPictures.dll")) && IsPluginEnabled("Moving Pictures");
            }
        }

        public static bool IsMPTVSeriesAvailableAndEnabled
        {
            get
            {
                return File.Exists(Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "MP-TVSeries.dll")) && IsPluginEnabled("MP-TV Series");
            }
        }

        public static bool IsMyFilmsAvailableAndEnabled
        {
            get
            {
                return File.Exists(Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "MyFilms.dll")) && IsPluginEnabled("MyFilms");
            }
        }

        public static bool IsMyAnimeAvailableAndEnabled
        {
            get
            {
                return File.Exists(Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "Anime2.dll")) && IsPluginEnabled("My Anime");
            }
        }

        public static bool IsMpNZBAvailableAndEnabled
        {
            get
            {
                return File.Exists(Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "mpNZB.dll")) && IsPluginEnabled("mpNZB");
            }
        }

        public static bool IsMyTorrentsAvailableAndEnabled
        {
            get
            {
                return File.Exists(Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "MyTorrents.dll")) && IsPluginEnabled("MyTorrents");
            }
        }

        public static TraktRateValue GetRateValue(int starRating, bool starRatingIsOutOf5 = false)
        {
            if (starRatingIsOutOf5)
                starRating = starRating*2;

            if (starRating >= TraktSettings.LoveMinimumValue)
                return TraktRateValue.love;

            if (starRating <= TraktSettings.HateMaximumValue)
                return TraktRateValue.hate;

            return TraktRateValue.unrate;
        }

        public static TraktRateValue GetRateValue(double starRating, bool starRatingIsOutOf5 = false)
        {
            return GetRateValue((int) starRating, starRatingIsOutOf5);
        }

        public static TraktRateValue GetRateValue(int? starRating, bool starRatingIsOutOf5 = false)
        {
            if (starRating == null)
                return TraktRateValue.unrate;
            return GetRateValue((int)starRating, starRatingIsOutOf5);
        }

        #endregion

        #region API Helpers

        #region Movie Watchlist

        public static void AddMovieToWatchList(string title, string year)
        {
            AddMovieToWatchList(title, year, null);
        }

        public static void AddMovieToWatchList(string title, string year, string imdbid)
        {
            AddMovieToWatchList(title, year, imdbid, false);
        }

        public static void AddMovieToWatchList(string title, string year, bool updateMovingPicturesFilters)
        {
            AddMovieToWatchList(title, year, null, updateMovingPicturesFilters);
        }

        /// <summary>
        /// Adds a movie to the current users Watch List
        /// </summary>
        /// <param name="title">title of movie</param>
        /// <param name="year">year of movie</param>
        /// <param name="imdbid">imdbid of movie</param>
        /// <param name="updateMovingPicturesFilters">set to true if movingpictures categories/filters should also be updated</param>
        public static void AddMovieToWatchList(string title, string year, string imdbid, bool updateMovingPicturesFilters)
        {
            if (!GUICommon.CheckLogin(false)) return;

            TraktMovieSync syncObject = BasicHandler.CreateMovieSyncData(title, year, imdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(obj as TraktMovieSync, TraktSyncModes.watchlist);
                if (response == null || response.Status != "success") return;
                if (updateMovingPicturesFilters && IsMovingPicturesAvailableAndEnabled)
                {
                    // Update Categories & Filters
                    MovingPictures.ClearWatchListCache();
                    MovingPictures.UpdateCategoriesAndFilters();
                }
                GUI.GUIWatchListMovies.ClearCache(TraktSettings.Username);
            })
            {
                IsBackground = true,
                Name = "Adding Movie to Watch List"
            };

            syncThread.Start(syncObject);
        }

        public static void RemoveMovieFromWatchList(string title, string year, string imdbid, bool updateMovingPicturesFilters)
        {
            if (!GUICommon.CheckLogin(false)) return;

            TraktMovieSync syncObject = BasicHandler.CreateMovieSyncData(title, year, imdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(obj as TraktMovieSync, TraktSyncModes.unwatchlist);
                if (response == null || response.Status != "success") return;
                if (updateMovingPicturesFilters && IsMovingPicturesAvailableAndEnabled)
                {
                    // Update Categories & Filters
                    MovingPictures.ClearWatchListCache();
                    MovingPictures.UpdateCategoriesAndFilters();
                }
                GUI.GUIWatchListMovies.ClearCache(TraktSettings.Username);
            })
            {
                IsBackground = true,
                Name = "Removing Movie From Watch List"
            };

            syncThread.Start(syncObject);
        }
        #endregion

        #region Show WatchList
        public static void AddShowToWatchList(string title, string year, string tvdbid)
        {
            TraktShowSync syncObject = BasicHandler.CreateShowSyncData(title, year, tvdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncShowWatchList((obj as TraktShowSync), TraktSyncModes.watchlist);
                if (response == null || response.Status != "success") return;
                GUI.GUIWatchListShows.ClearCache(TraktSettings.Username);
            })
            {
                IsBackground = true,
                Name = "Adding Show to Watch List"
            };

            syncThread.Start(syncObject);
        }

        public static void RemoveShowFromWatchList(string title, string year, string tvdbid)
        {
            TraktShowSync syncObject = BasicHandler.CreateShowSyncData(title, year, tvdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncShowWatchList((obj as TraktShowSync), TraktSyncModes.unwatchlist);
                if (response == null || response.Status != "success") return;
                GUI.GUIWatchListShows.ClearCache(TraktSettings.Username);
            })
            {
                IsBackground = true,
                Name = "Removing Show From Watch List"
            };

            syncThread.Start(syncObject);
        }
        #endregion

        #region Episode WatchList
        public static void AddEpisodeToWatchList(string title, string year, string tvdbid, string seasonidx, string episodeidx)
        {
            TraktEpisodeSync syncObject = BasicHandler.CreateEpisodeSyncData(title, year, tvdbid, seasonidx, episodeidx);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncEpisodeWatchList((obj as TraktEpisodeSync), TraktSyncModes.watchlist);
                if (response == null || response.Status != "success") return;
            })
            {
                IsBackground = true,
                Name = "Adding Episode to Watch List"
            };

            syncThread.Start(syncObject);
        }

        public static void RemoveEpisodeFromWatchList(string title, string year, string tvdbid, string seasonidx, string episodeidx)
        {
            TraktEpisodeSync syncObject = BasicHandler.CreateEpisodeSyncData(title, year, tvdbid, seasonidx, episodeidx);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncEpisodeWatchList((obj as TraktEpisodeSync), TraktSyncModes.unwatchlist);
                if (response == null || response.Status != "success") return;
            })
            {
                IsBackground = true,
                Name = "Removing Episode From Watch List"
            };

            syncThread.Start(syncObject);
        }
        #endregion

        #region Add/Remove Movie in List

        public static void AddRemoveMovieInUserList(string title, string year, string imdbid, bool remove)
        {
            AddRemoveMovieInUserList(TraktSettings.Username, title, year, imdbid, remove);
        }

        public static void AddRemoveMovieInUserList(string username, string title, string year, string imdbid, bool remove)
        {
            if (!GUICommon.CheckLogin(false)) return;

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                return TraktLists.GetListsForUser(username);
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    IEnumerable<TraktUserList> customlists = result as IEnumerable<TraktUserList>;

                    // get slug of lists selected
                    List<string> slugs = TraktLists.GetUserListSelections(customlists.ToList());
                    if (slugs == null || slugs.Count == 0) return;

                    TraktListItem item = new TraktListItem
                    {
                        Type = TraktItemType.movie.ToString(),
                        Title = title,
                        Year = Convert.ToInt32(year),
                        ImdbId = imdbid
                    };

                    AddRemoveItemInList(slugs, item, remove);
                }
            }, Translation.GettingLists, true);
        }

        #endregion

        #region Add/Remove Show in List

        public static void AddRemoveShowInUserList(string title, string year, string tvdbid, bool remove)
        {
            AddRemoveShowInUserList(TraktSettings.Username, title, year, tvdbid, remove);
        }

        public static void AddRemoveShowInUserList(string username, string title, string year, string tvdbid, bool remove)
        {
            if (!GUICommon.CheckLogin(false)) return;

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                return TraktLists.GetListsForUser(username);
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    IEnumerable<TraktUserList> customlists = result as IEnumerable<TraktUserList>;

                    // get slug of lists selected
                    List<string> slugs = TraktLists.GetUserListSelections(customlists.ToList());
                    if (slugs == null || slugs.Count == 0) return;

                    TraktListItem item = new TraktListItem
                    {
                        Type = TraktItemType.show.ToString(),
                        Title = title,
                        Year = Convert.ToInt32(year),
                        TvdbId = tvdbid
                    };

                    AddRemoveItemInList(slugs, item, remove);
                }
            }, Translation.GettingLists, true);
        }

        #endregion

        #region Add/Remove Season in List

        public static void AddRemoveSeasonInUserList(string title, string year, string season, string tvdbid, bool remove)
        {
            AddRemoveSeasonInUserList(TraktSettings.Username, title, year, season, tvdbid, remove);
        }

        public static void AddRemoveSeasonInUserList(string username, string title, string year, string season, string tvdbid, bool remove)
        {
            if (!GUICommon.CheckLogin(false)) return;

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                return TraktLists.GetListsForUser(username);
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    IEnumerable<TraktUserList> customlists = result as IEnumerable<TraktUserList>;

                    // get slug of lists selected
                    List<string> slugs = TraktLists.GetUserListSelections(customlists.ToList());
                    if (slugs == null || slugs.Count == 0) return;

                    TraktListItem item = new TraktListItem
                    {
                        Type = TraktItemType.season.ToString(),
                        Title = title,
                        Year = Convert.ToInt32(year),
                        Season = Convert.ToInt32(season),
                        TvdbId = tvdbid
                    };

                    AddRemoveItemInList(slugs, item, remove);
                }
            }, Translation.GettingLists, true);
        }

        #endregion

        #region Add/Remove Episode in List

        public static void AddRemoveEpisodeInUserList(string title, string year, string season, string episode, string tvdbid, bool remove)
        {
            AddRemoveEpisodeInUserList(TraktSettings.Username, title, year, season, episode, tvdbid, remove);
        }

        public static void AddRemoveEpisodeInUserList(string username, string title, string year, string season, string episode, string tvdbid, bool remove)
        {
            if (!GUICommon.CheckLogin(false)) return;

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                return TraktLists.GetListsForUser(username);
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    IEnumerable<TraktUserList> customlists = result as IEnumerable<TraktUserList>;

                    // get slug of lists selected
                    List<string> slugs = TraktLists.GetUserListSelections(customlists.ToList());
                    if (slugs == null || slugs.Count == 0) return;

                    TraktListItem item = new TraktListItem
                    {
                        Type = TraktItemType.episode.ToString(),
                        Title = title,
                        Year = Convert.ToInt32(year),
                        Season = Convert.ToInt32(season),
                        Episode = Convert.ToInt32(episode),
                        TvdbId = tvdbid
                    };

                    AddRemoveItemInList(slugs, item, remove);
                }
            }, Translation.GettingLists, true);
        }

        #endregion

        #region Related Movies
        public static void ShowRelatedMovies(string imdbid, string title, string year)
        {
            if (!File.Exists(GUIGraphicsContext.Skin + @"\Trakt.Related.Movies.xml"))
            {
                // let user know they need to update skin or harass skin author
                GUIUtils.ShowOKDialog(GUIUtils.PluginName(), Translation.SkinOutOfDate);
                return;
            }
           
            RelatedMovie relatedMovie = new RelatedMovie
            {
                IMDbId = imdbid,
                Title = title,
                Year = Convert.ToInt32(string.IsNullOrEmpty(year) ? "0" : year)
            };
            GUIRelatedMovies.relatedMovie = relatedMovie;
            GUIWindowManager.ActivateWindow((int)TraktGUIWindows.RelatedMovies);
        }
        #endregion

        #region Related Shows
        public static void ShowRelatedShows(string tvdbid, string title)
        {
            if (!File.Exists(GUIGraphicsContext.Skin + @"\Trakt.Related.Shows.xml"))
            {
                // let user know they need to update skin or harass skin author
                GUIUtils.ShowOKDialog(GUIUtils.PluginName(), Translation.SkinOutOfDate);
                return;
            }

            RelatedShow relatedShow = new RelatedShow
            {
                TVDbId = tvdbid,
                Title = title                
            };
            GUIRelatedShows.relatedShow = relatedShow;
            GUIWindowManager.ActivateWindow((int)TraktGUIWindows.RelatedShows);
        }
        #endregion

        #region Movie Shouts
        public static void ShowMovieShouts(string imdb, string title, string year, string fanart)
        {
            if (!File.Exists(GUIGraphicsContext.Skin + @"\Trakt.Shouts.xml"))
            {
                // let user know they need to update skin or harass skin author
                GUIUtils.ShowOKDialog(GUIUtils.PluginName(), Translation.SkinOutOfDate);
                return;
            }

            MovieShout movieInfo = new MovieShout
            {
                IMDbId = imdb,
                Title = title,
                Year = year
            };
            GUIShouts.ShoutType = GUIShouts.ShoutTypeEnum.movie;
            GUIShouts.MovieInfo = movieInfo;
            GUIShouts.Fanart = fanart;
            GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Shouts);
        }
        #endregion

        #region Show Shouts
        public static void ShowTVShowShouts(string tvdb, string title, string fanart)
        {
            if (!File.Exists(GUIGraphicsContext.Skin + @"\Trakt.Shouts.xml"))
            {
                // let user know they need to update skin or harass skin author
                GUIUtils.ShowOKDialog(GUIUtils.PluginName(), Translation.SkinOutOfDate);
                return;
            }

            ShowShout seriesInfo = new ShowShout
            {
                TVDbId = tvdb,
                Title = title,
            };
            GUIShouts.ShoutType = GUIShouts.ShoutTypeEnum.show;
            GUIShouts.ShowInfo = seriesInfo;
            GUIShouts.Fanart = fanart;
            GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Shouts);
        }
        #endregion

        #region Episode Shouts
        public static void ShowEpisodeShouts(string tvdb, string title, string season, string episode, string fanart)
        {
            if (!File.Exists(GUIGraphicsContext.Skin + @"\Trakt.Shouts.xml"))
            {
                // let user know they need to update skin or harass skin author
                GUIUtils.ShowOKDialog(GUIUtils.PluginName(), Translation.SkinOutOfDate);
                return;
            }

            EpisodeShout episodeInfo = new EpisodeShout
            {
                TVDbId = tvdb,
                Title = title,
                SeasonIdx = season,
                EpisodeIdx = episode
            };
            GUIShouts.ShoutType = GUIShouts.ShoutTypeEnum.episode;
            GUIShouts.EpisodeInfo = episodeInfo;
            GUIShouts.Fanart = fanart;
            GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Shouts);
        }
        #endregion

        #region Movie Watched/UnWatched
        public static void MarkMovieAsWatched(string imdbid, string title, string year)
        {
            TraktMovieSync syncObject = BasicHandler.CreateMovieSyncData(title, year, imdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(obj as TraktMovieSync, TraktSyncModes.seen);
            })
            {
                IsBackground = true,
                Name = "Mark Movie as Watched"
            };

            syncThread.Start(syncObject);
        }

        public static void MarkMovieAsUnWatched(string imdbid, string title, string year)
        {
            TraktMovieSync syncObject = BasicHandler.CreateMovieSyncData(title, year, imdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(obj as TraktMovieSync, TraktSyncModes.unseen);
            })
            {
                IsBackground = true,
                Name = "Mark Movie as UnWatched"
            };

            syncThread.Start(syncObject);
        }
        #endregion

        #region Episode Watched/UnWatched
        public static void MarkEpisodeAsWatched(string title, string year, string tvdbid, string seasonidx, string episodeidx)
        {
            TraktEpisodeSync syncObject = BasicHandler.CreateEpisodeSyncData(title, year, tvdbid, seasonidx, episodeidx);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncEpisodeWatchList((obj as TraktEpisodeSync), TraktSyncModes.seen);
                if (response == null || response.Status != "success") return;
            })
            {
                IsBackground = true,
                Name = "Mark Episode as Watched"
            };

            syncThread.Start(syncObject);
        }

        public static void MarkEpisodeAsUnWatched(string title, string year, string tvdbid, string seasonidx, string episodeidx)
        {
            TraktEpisodeSync syncObject = BasicHandler.CreateEpisodeSyncData(title, year, tvdbid, seasonidx, episodeidx);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncEpisodeWatchList((obj as TraktEpisodeSync), TraktSyncModes.unseen);
                if (response == null || response.Status != "success") return;
            })
            {
                IsBackground = true,
                Name = "Mark Episode as UnWatched"
            };

            syncThread.Start(syncObject);
        }
        #endregion

        #region Movie Library/UnLibrary
        public static void AddMovieToLibrary(string imdbid, string title, string year)
        {
            TraktMovieSync syncObject = BasicHandler.CreateMovieSyncData(title, year, imdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(obj as TraktMovieSync, TraktSyncModes.library);
            })
            {
                IsBackground = true,
                Name = "Add Movie to Library"
            };

            syncThread.Start(syncObject);
        }

        public static void RemoveMovieFromLibrary(string imdbid, string title, string year)
        {
            TraktMovieSync syncObject = BasicHandler.CreateMovieSyncData(title, year, imdbid);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(obj as TraktMovieSync, TraktSyncModes.unlibrary);
            })
            {
                IsBackground = true,
                Name = "Remove Movie from Library"
            };

            syncThread.Start(syncObject);
        }
        #endregion

        #region Episode Library/UnLibrary
        public static void AddEpisodeToLibrary(string title, string year, string tvdbid, string seasonidx, string episodeidx)
        {
            TraktEpisodeSync syncObject = BasicHandler.CreateEpisodeSyncData(title, year, tvdbid, seasonidx, episodeidx);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncEpisodeWatchList((obj as TraktEpisodeSync), TraktSyncModes.library);
                if (response == null || response.Status != "success") return;
            })
            {
                IsBackground = true,
                Name = "Add Episode to Library"
            };

            syncThread.Start(syncObject);
        }

        public static void RemoveEpisodeFromLibrary(string title, string year, string tvdbid, string seasonidx, string episodeidx)
        {
            TraktEpisodeSync syncObject = BasicHandler.CreateEpisodeSyncData(title, year, tvdbid, seasonidx, episodeidx);
            if (syncObject == null) return;

            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktResponse response = TraktAPI.TraktAPI.SyncEpisodeWatchList((obj as TraktEpisodeSync), TraktSyncModes.unlibrary);
                if (response == null || response.Status != "success") return;
            })
            {
                IsBackground = true,
                Name = "Remove Episode From Library"
            };

            syncThread.Start(syncObject);
        }
        #endregion

        #endregion

        #region Internal Helpers

        internal static void AddRemoveItemInList(string slug, TraktListItem item, bool remove)
        {
            AddRemoveItemInList(new List<string> { slug }, new List<TraktListItem>() { item }, remove);
        }

        internal static void AddRemoveItemInList(List<string> slugs, TraktListItem item, bool remove)
        {
            AddRemoveItemInList(slugs, new List<TraktListItem>() { item }, remove);
        }

        internal static void AddRemoveItemInList(List<string> slugs, List<TraktListItem> items, bool remove)
        {
            Thread listThread = new Thread(delegate(object obj)
            {
                foreach (var slug in slugs)
                {
                    TraktList list = new TraktList
                    {
                        UserName = TraktSettings.Username,
                        Password = TraktSettings.Password,
                        Slug = slug,
                        Items = items
                    };
                    TraktSyncResponse response = null;
                    if (!remove)
                        response = TraktAPI.TraktAPI.ListAddItems(list);
                    else
                        response = TraktAPI.TraktAPI.ListDeleteItems(list);

                    TraktAPI.TraktAPI.LogTraktResponse<TraktSyncResponse>(response);
                    if (response.Status == "success")
                    {
                        // clear current items in any lists
                        // list items will be refreshed online if we try to request them
                       TraktLists.ClearItemsInList(TraktSettings.Username, slug);
                    }
                }
            })
            {
                Name = remove ? "Remove Item from List" : "Add Item to List",
                IsBackground = true
            };

            listThread.Start();
        }

        #endregion
    }
}
