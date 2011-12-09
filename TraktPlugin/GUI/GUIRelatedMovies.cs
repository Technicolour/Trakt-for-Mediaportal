﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using MediaPortal.Video.Database;
using MediaPortal.GUI.Video;
using Action = MediaPortal.GUI.Library.Action;
using MediaPortal.Util;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;

namespace TraktPlugin.GUI
{
    public class RelatedMovie
    {
        public string Title { get; set; }
        public int Year { get; set; }
        public string IMDbId { get; set; }

        public string Slug
        {
            get
            {
                if (!string.IsNullOrEmpty(TraktHandlers.BasicHandler.GetProperMovieImdbId(IMDbId))) return IMDbId;
                if (string.IsNullOrEmpty(Title)) return string.Empty;
                return string.Format("{0} {1}", Title, Year).ToSlug();
            }
        }

    }

    public class GUIRelatedMovies : GUIWindow
    {
        #region Skin Controls

        [SkinControl(2)]
        protected GUIButtonControl layoutButton = null;

        [SkinControl(3)]
        protected GUICheckButton hideWatchedButton = null;

        [SkinControl(50)]
        protected GUIFacadeControl Facade = null;

        [SkinControlAttribute(60)]
        protected GUIImage FanartBackground = null;

        [SkinControlAttribute(61)]
        protected GUIImage FanartBackground2 = null;

        [SkinControlAttribute(62)]
        protected GUIImage loadingImage = null;

        #endregion

        #region Enums

        enum Layout
        {
            List = 0,
            SmallIcons = 1,
            LargeIcons = 2,
            Filmstrip = 3,
        }

        enum TrailerSite
        {
            IMDb,
            iTunes,
            YouTube
        }

        enum ContextMenuItem
        {
            HideShowWatched,
            MarkAsWatched,
            MarkAsUnWatched,
            AddToWatchList,
            RemoveFromWatchList,
            AddToList,
            AddToLibrary,
            RemoveFromLibrary,
            Related,
            Rate,
            Shouts,
            ChangeLayout,
            Trailers,
            SearchWithMpNZB,
            SearchTorrent
        }

        #endregion

        #region Constructor

        public GUIRelatedMovies()
        {
            backdrop = new ImageSwapper();
            backdrop.PropertyOne = "#Trakt.RelatedMovies.Fanart.1";
            backdrop.PropertyTwo = "#Trakt.RelatedMovies.Fanart.2";
        }

        #endregion

        #region Public Variables

        public static RelatedMovie relatedMovie { get; set; }

        #endregion

        #region Private Variables

        bool StopDownload { get; set; }
        private Layout CurrentLayout { get; set; }
        private ImageSwapper backdrop;
        DateTime LastRequest = new DateTime();
        int PreviousSelectedIndex = 0;
        Dictionary<string, IEnumerable<TraktMovie>> dictRelatedMovies = new Dictionary<string, IEnumerable<TraktMovie>>();
        bool HideWatched = false;
        bool SendingWatchedToTrakt = false;
        bool RelationChanged = false;

        IEnumerable<TraktMovie> RelatedMovies
        {
            get
            {
                if (!dictRelatedMovies.Keys.Contains(relatedMovie.Slug) || LastRequest < DateTime.UtcNow.Subtract(new TimeSpan(0, TraktSettings.WebRequestCacheMinutes, 0)))
                {
                    _RelatedMovies = TraktAPI.TraktAPI.GetRelatedMovies(relatedMovie.Slug, HideWatched);
                    if (dictRelatedMovies.Keys.Contains(relatedMovie.Slug)) dictRelatedMovies.Remove(relatedMovie.Slug);
                    dictRelatedMovies.Add(relatedMovie.Slug, _RelatedMovies);
                    LastRequest = DateTime.UtcNow;
                    PreviousSelectedIndex = 0;
                }
                return dictRelatedMovies[relatedMovie.Slug];
            }
        }
        private IEnumerable<TraktMovie> _RelatedMovies = null;

        #endregion

        #region Base Overrides

        public override int GetID
        {
            get
            {
                return (int)TraktGUIWindows.RelatedMovies;
            }
        }

        public override bool Init()
        {
            return Load(GUIGraphicsContext.Skin + @"\Trakt.Related.Movies.xml");
        }

        protected override void OnPageLoad()
        {
            // Clear GUI Properties
            ClearProperties();

            // Init Properties
            InitProperties();

            // Load Related Movies
            LoadRelatedMovies();
        }

        protected override void OnPageDestroy(int new_windowId)
        {
            StopDownload = true;            
            ClearProperties();

            if (RelationChanged)
                PreviousSelectedIndex = 0;
            else 
                PreviousSelectedIndex = Facade.SelectedListItemIndex;

            // save settings
            TraktSettings.RelatedMoviesDefaultLayout = (int)CurrentLayout;
            TraktSettings.HideWatchedRelatedMovies = HideWatched;

            base.OnPageDestroy(new_windowId);
        }

        protected override void OnClicked(int controlId, GUIControl control, Action.ActionType actionType)
        {
            // wait for any background action to finish
            if (GUIBackgroundTask.Instance.IsBusy) return;

            switch (controlId)
            {
                // Facade
                case (50):
                    if (actionType == Action.ActionType.ACTION_SELECT_ITEM)
                    {
                        CheckAndPlayMovie(true);
                    }
                    break;

                // Layout Button
                case (2):
                    ShowLayoutMenu();
                    break;
                
                // Hide Watched Button
                case (3):
                    HideWatched = hideWatchedButton.Selected;
                    dictRelatedMovies.Remove(relatedMovie.Slug);
                    LoadRelatedMovies();
                    GUIControl.FocusControl((int)TraktGUIWindows.RelatedMovies, Facade.GetID);
                    break;

                default:
                    break;
            }
            base.OnClicked(controlId, control, actionType);
        }

        public override void OnAction(Action action)
        {
            switch (action.wID)
            {
                case Action.ActionType.ACTION_PLAY:
                case Action.ActionType.ACTION_MUSIC_PLAY:
                    if (!GUIBackgroundTask.Instance.IsBusy)
                    {
                        CheckAndPlayMovie(false);
                    }
                    break;
                default:
                    base.OnAction(action);
                    break;
            }
        }

        protected override void OnShowContextMenu()
        {
            if (GUIBackgroundTask.Instance.IsBusy) return;

            GUIListItem selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            TraktMovie selectedMovie = (TraktMovie)selectedItem.TVTag;

            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem listItem = null;

            // Hide/Show Watched items
            listItem = new GUIListItem(HideWatched ? Translation.ShowWatched : Translation.HideWatched);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.HideShowWatched;

            // Mark As Watched
            if (!selectedMovie.Watched)
            {
                listItem = new GUIListItem(Translation.MarkAsWatched);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.MarkAsWatched;
            }

            // Mark As UnWatched
            if (selectedMovie.Watched)
            {
                listItem = new GUIListItem(Translation.MarkAsUnWatched);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.MarkAsUnWatched;
            }

            // Add/Remove Watch List            
            if (!selectedMovie.InWatchList)
            {
                listItem = new GUIListItem(Translation.AddToWatchList);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.AddToWatchList;
            }
            else
            {
                listItem = new GUIListItem(Translation.RemoveFromWatchList);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.RemoveFromWatchList;
            }

            // Add to Custom list
            listItem = new GUIListItem(Translation.AddToList + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.AddToList;

            // Add to Library
            // Don't allow if it will be removed again on next sync
            // movie could be part of a DVD collection
            if (!selectedMovie.InCollection && !TraktSettings.KeepTraktLibraryClean)
            {
                listItem = new GUIListItem(Translation.AddToLibrary);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.AddToLibrary;
            }

            if (selectedMovie.InCollection)
            {
                listItem = new GUIListItem(Translation.RemoveFromLibrary);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.RemoveFromLibrary;
            }

            // Related Movies
            listItem = new GUIListItem(Translation.RelatedMovies + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Related;

            // Rate Movie
            listItem = new GUIListItem(Translation.RateMovie);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Rate;

            // Shouts
            listItem = new GUIListItem(Translation.Shouts + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Shouts;

            #if MP12
            // Trailers
            if (TraktHelper.IsOnlineVideosAvailableAndEnabled)
            {
                listItem = new GUIListItem(Translation.Trailers);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.Trailers;
            }
            #endif

            // Change Layout
            listItem = new GUIListItem(Translation.ChangeLayout);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.ChangeLayout;

            #if MP12
            if (!selectedMovie.InCollection && TraktHelper.IsMpNZBAvailableAndEnabled)
            {
                // Search for movie with mpNZB
                listItem = new GUIListItem(Translation.SearchWithMpNZB);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.SearchWithMpNZB;
            }
            #endif

            #if MP12
            if (!selectedMovie.InCollection && TraktHelper.IsMyTorrentsAvailableAndEnabled)
            {
                // Search for movie with MyTorrents
                listItem = new GUIListItem(Translation.SearchTorrent);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.SearchTorrent;
            }
            #endif

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)ContextMenuItem.HideShowWatched):
                    HideWatched = !HideWatched;
                    if (hideWatchedButton != null) hideWatchedButton.Selected = HideWatched;
                    dictRelatedMovies.Remove(relatedMovie.Slug);
                    LoadRelatedMovies();
                    break;

                case ((int)ContextMenuItem.MarkAsWatched):                    
                    MarkMovieAsWatched(selectedMovie);
                    if (!HideWatched)
                    {
                        if (selectedMovie.Plays == 0) selectedMovie.Plays = 1;
                        selectedMovie.Watched = true;
                        selectedItem.IsPlayed = true;
                        OnMovieSelected(selectedItem, Facade);
                        selectedMovie.Images.NotifyPropertyChanged("PosterImageFilename");
                    }
                    else
                    {
                        dictRelatedMovies.Remove(relatedMovie.Slug);
                        LoadRelatedMovies();
                    }
                    break;

                case ((int)ContextMenuItem.MarkAsUnWatched):
                    MarkMovieAsUnWatched(selectedMovie);
                    selectedMovie.Watched = false;
                    selectedItem.IsPlayed = false;
                    OnMovieSelected(selectedItem, Facade);
                    selectedMovie.Images.NotifyPropertyChanged("PosterImageFilename");
                    break;

                case ((int)ContextMenuItem.AddToWatchList):
                    AddMovieToWatchList(selectedMovie);
                    selectedMovie.InWatchList = true;
                    OnMovieSelected(selectedItem, Facade);
                    selectedMovie.Images.NotifyPropertyChanged("PosterImageFilename");
                    GUIWatchListMovies.ClearCache(TraktSettings.Username);
                    break;

                case ((int)ContextMenuItem.RemoveFromWatchList):
                    RemoveMovieFromWatchList(selectedMovie);
                    selectedMovie.InWatchList = false;
                    OnMovieSelected(selectedItem, Facade);
                    selectedMovie.Images.NotifyPropertyChanged("PosterImageFilename");
                    GUIWatchListMovies.ClearCache(TraktSettings.Username);
                    break;

                case ((int)ContextMenuItem.AddToList):
                    TraktHelper.AddRemoveMovieInUserList(selectedMovie.Title, selectedMovie.Year, selectedMovie.Imdb, false);
                    break;

                case ((int)ContextMenuItem.AddToLibrary):
                    AddMovieToLibrary(selectedMovie);
                    selectedMovie.InCollection = true;
                    OnMovieSelected(selectedItem, Facade);
                    selectedMovie.Images.NotifyPropertyChanged("PosterImageFilename");
                    break;

                case ((int)ContextMenuItem.RemoveFromLibrary):
                    RemoveMovieFromLibrary(selectedMovie);
                    selectedMovie.InCollection = false;
                    OnMovieSelected(selectedItem, Facade);
                    selectedMovie.Images.NotifyPropertyChanged("PosterImageFilename");
                    break;

                case ((int)ContextMenuItem.Related):
                    RelatedMovie relMovie = new RelatedMovie
                    {
                        Title = selectedMovie.Title,
                        IMDbId = selectedMovie.Imdb,
                        Year = Convert.ToInt32(selectedMovie.Year)
                    };
                    relatedMovie = relMovie;
                    GUIUtils.SetProperty("#Trakt.Related.Movie", relMovie.Title);
                    LoadRelatedMovies();
                    RelationChanged = true;
                    break;

                case ((int)ContextMenuItem.Rate):
                    RateMovie(selectedMovie);
                    OnMovieSelected(selectedItem, Facade);
                    selectedMovie.Images.NotifyPropertyChanged("PosterImageFilename");
                    break;

                case ((int)ContextMenuItem.Shouts):
                    GUIShouts.ShoutType = GUIShouts.ShoutTypeEnum.movie;
                    GUIShouts.MovieInfo = new MovieShout { IMDbId = selectedMovie.Imdb, TMDbId = selectedMovie.Tmdb, Title = selectedMovie.Title, Year = selectedMovie.Year };
                    GUIShouts.Fanart = selectedMovie.Images.FanartImageFilename;
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Shouts);
                    break;

                #if MP12
                case ((int)ContextMenuItem.Trailers):
                    ShowTrailersMenu(selectedMovie);
                    break;
                #endif

                case ((int)ContextMenuItem.ChangeLayout):
                    ShowLayoutMenu();
                    break;

                #if MP12
                case ((int)ContextMenuItem.SearchWithMpNZB):
                    string loadingParam = string.Format("search:{0}", selectedMovie.Title);
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MpNZB, loadingParam);
                    break;
                #endif

                #if MP12
                case ((int)ContextMenuItem.SearchTorrent):
                    string loadPar = selectedMovie.Title;
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyTorrents, loadPar);
                    break;
                #endif

                default:
                    break;
            }

            base.OnShowContextMenu();
        }

        #endregion

        #region Private Methods

        private void CheckAndPlayMovie(bool jumpTo)
        {
            GUIListItem selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            TraktMovie selectedMovie = selectedItem.TVTag as TraktMovie;
            if (selectedMovie == null) return;

            string title = selectedMovie.Title;
            string imdbid = selectedMovie.Imdb;
            int year = Convert.ToInt32(selectedMovie.Year);

            GUICommon.CheckAndPlayMovie(jumpTo, title, year, imdbid);
        }

        private TraktMovieSync CreateSyncData(TraktMovie movie)
        {
            if (movie == null) return null;

            List<TraktMovieSync.Movie> movies = new List<TraktMovieSync.Movie>();

            TraktMovieSync.Movie syncMovie = new TraktMovieSync.Movie
            {
                IMDBID = movie.Imdb,
                Title = movie.Title,
                Year = movie.Year
            };
            movies.Add(syncMovie);

            TraktMovieSync syncData = new TraktMovieSync
            {
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password,
                MovieList = movies
            };

            return syncData;
        }

        private void AddMovieToWatchList(TraktMovie movie)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(obj as TraktMovie), TraktSyncModes.watchlist);
            })
            {
                IsBackground = true,
                Name = "Adding Movie to Watch List"
            };

            syncThread.Start(movie);
        }

        private void RemoveMovieFromWatchList(TraktMovie movie)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(obj as TraktMovie), TraktSyncModes.unwatchlist);
            })
            {
                IsBackground = true,
                Name = "Removing Movie from Watch List"
            };

            syncThread.Start(movie);
        }

        private void MarkMovieAsWatched(TraktMovie movie)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                SendingWatchedToTrakt = true;
                TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(obj as TraktMovie), TraktSyncModes.seen);
                SendingWatchedToTrakt = false;
            })
            {
                IsBackground = true,
                Name = "Mark Movie as Watched"
            };

            syncThread.Start(movie);
        }

        private void MarkMovieAsUnWatched(TraktMovie movie)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(obj as TraktMovie), TraktSyncModes.unseen);
            })
            {
                IsBackground = true,
                Name = "Mark Movie as UnWatched"
            };

            syncThread.Start(movie);
        }

        private void AddMovieToLibrary(TraktMovie movie)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(obj as TraktMovie), TraktSyncModes.library);
            })
            {
                IsBackground = true,
                Name = "Add Movie to Library"
            };

            syncThread.Start(movie);
        }

        private void RemoveMovieFromLibrary(TraktMovie movie)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(obj as TraktMovie), TraktSyncModes.unlibrary);
            })
            {
                IsBackground = true,
                Name = "Remove Movie From Library"
            };

            syncThread.Start(movie);
        }

        private void RateMovie(TraktMovie movie)
        {
            // default rating to love if not already set
            TraktRateMovie rateObject = new TraktRateMovie
            {
                IMDBID = movie.Imdb,
                Title = movie.Title,
                Year = movie.Year,
                Rating = movie.Rating,
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password
            };

            string prevRating = movie.Rating;
            movie.Rating = GUIUtils.ShowRateDialog<TraktRateMovie>(rateObject);

            // if previous rating not equal to current rating then 
            // update skin properties to reflect changes so we dont
            // need to re-request from server
            if (prevRating != movie.Rating)
            {
                if (prevRating == "false")
                {
                    movie.Ratings.Votes++;
                    if (movie.Rating == "love")
                        movie.Ratings.LovedCount++;
                    else
                        movie.Ratings.HatedCount++;
                }

                if (prevRating == "love")
                {
                    movie.Ratings.LovedCount--;
                    movie.Ratings.HatedCount++;
                }

                if (prevRating == "hate")
                {
                    movie.Ratings.LovedCount++;
                    movie.Ratings.HatedCount--;
                }

                movie.Ratings.Percentage = (int)Math.Round(100 * (movie.Ratings.LovedCount / (float)movie.Ratings.Votes));
            }
        }

        #if MP12
        private void ShowTrailersMenu(TraktMovie movie)
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(Translation.Trailer);

            foreach (TrailerSite site in Enum.GetValues(typeof(TrailerSite)))
            {
                string menuItem = Enum.GetName(typeof(TrailerSite), site);
                GUIListItem pItem = new GUIListItem(menuItem);
                dlg.Add(pItem);
            }

            dlg.DoModal(GUIWindowManager.ActiveWindow);

            if (dlg.SelectedLabel >= 0)
            {
                string siteUtil = string.Empty;
                string searchParam = string.Empty;

                switch (dlg.SelectedLabelText)
                {
                    case ("IMDb"):
                        siteUtil = "IMDb Movie Trailers";
                        if (!string.IsNullOrEmpty(movie.Imdb))
                            // Exact search
                            searchParam = movie.Imdb;
                        else
                            searchParam = movie.Title;
                        break;

                    case ("iTunes"):
                        siteUtil = "iTunes Movie Trailers";
                        searchParam = movie.Title;
                        break;

                    case ("YouTube"):
                        siteUtil = "YouTube";
                        searchParam = movie.Title;
                        break;
                }

                string loadingParam = string.Format("site:{0}|search:{1}|return:Locked", siteUtil, searchParam);
                // Launch OnlineVideos Trailer search
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParam);
            }
        }
        #endif

        private void ShowLayoutMenu()
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(GetLayoutTranslation(CurrentLayout));

            foreach (Layout layout in Enum.GetValues(typeof(Layout)))
            {
                string menuItem = GetLayoutTranslation(layout);
                GUIListItem pItem = new GUIListItem(menuItem);
                if (layout == CurrentLayout) pItem.Selected = true;
                dlg.Add(pItem);
            }

            dlg.DoModal(GUIWindowManager.ActiveWindow);

            if (dlg.SelectedLabel >= 0)
            {
                CurrentLayout = (Layout)dlg.SelectedLabel;
                Facade.SetCurrentLayout(Enum.GetName(typeof(Layout), CurrentLayout));
                GUIControl.SetControlLabel(GetID, layoutButton.GetID, GetLayoutTranslation(CurrentLayout));
            }
        }

        private string GetLayoutTranslation(Layout layout)
        {
            string strLine = string.Empty;
            switch (layout)
            {
                case Layout.List:
                    strLine = GUILocalizeStrings.Get(101);
                    break;
                case Layout.SmallIcons:
                    strLine = GUILocalizeStrings.Get(100);
                    break;
                case Layout.LargeIcons:
                    strLine = GUILocalizeStrings.Get(417);
                    break;
                case Layout.Filmstrip:
                    strLine = GUILocalizeStrings.Get(733);
                    break;
            }
            return strLine;
        }

        private void LoadRelatedMovies()
        {
            GUIUtils.SetProperty("#Trakt.Items", string.Empty);

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                if (hideWatchedButton != null)
                {
                    GUIControl.DisableControl((int)TraktGUIWindows.RelatedMovies, hideWatchedButton.GetID);
                }

                if (HideWatched)
                {
                    // wait until watched item has been sent to trakt or timesout (10secs)
                    while (SendingWatchedToTrakt) Thread.Sleep(500);
                }
                return RelatedMovies;
            },
            delegate(bool success, object result)
            {
                if (hideWatchedButton != null)
                {
                    GUIControl.EnableControl((int)TraktGUIWindows.RelatedMovies, hideWatchedButton.GetID);
                }

                if (success)
                {
                    IEnumerable<TraktMovie> movies = result as IEnumerable<TraktMovie>;
                    SendRelatedMoviesToFacade(movies);
                }
            }, Translation.GettingRelatedMovies, true);
        }

        private void SendRelatedMoviesToFacade(IEnumerable<TraktMovie> movies)
        {
            // clear facade
            GUIControl.ClearControl(GetID, Facade.GetID);

            if (movies.Count() == 0)
            {
                string title = string.IsNullOrEmpty(relatedMovie.Title) ? relatedMovie.IMDbId : relatedMovie.Title;
                GUIUtils.ShowNotifyDialog(GUIUtils.PluginName(), string.Format(Translation.NoRelatedMovies, title));
                GUIWindowManager.ShowPreviousWindow();
                return;
            }

            int itemId = 0;
            List<TraktMovie.MovieImages> movieImages = new List<TraktMovie.MovieImages>();

            // Add each movie mark remote if not in collection            
            foreach (var movie in movies)
            {
                GUITraktRelatedMovieListItem item = new GUITraktRelatedMovieListItem(movie.Title);

                item.Label2 = movie.Year;
                item.TVTag = movie;
                item.Item = movie.Images;
                item.IsPlayed = movie.Watched;
                item.ItemId = Int32.MaxValue - itemId;
                // movie in collection doesnt nessararily mean
                // that the movie is locally available on this computer
                // as 'keep library clean' might not be enabled
                //item.IsRemote = !movie.InCollection;
                item.IconImage = "defaultVideo.png";
                item.IconImageBig = "defaultVideoBig.png";
                item.ThumbnailImage = "defaultVideoBig.png";
                item.OnItemSelected += OnMovieSelected;
                Utils.SetDefaultIcons(item);
                Facade.Add(item);
                itemId++;

                // add image for download
                movieImages.Add(movie.Images);
            }

            // Set Facade Layout
            Facade.SetCurrentLayout(Enum.GetName(typeof(Layout), CurrentLayout));
            GUIControl.FocusControl(GetID, Facade.GetID);

            Facade.SelectIndex(PreviousSelectedIndex);

            // set facade properties
            GUIUtils.SetProperty("#itemcount", movies.Count().ToString());
            GUIUtils.SetProperty("#Trakt.Items", string.Format("{0} {1}", movies.Count().ToString(), movies.Count() > 1 ? Translation.Movies : Translation.Movie));            

            // Download movie images Async and set to facade
            GetImages(movieImages);
        }

        private void SetProperty(string property, string value)
        {
            string propertyValue = string.IsNullOrEmpty(value) ? "N/A" : value;
            GUIUtils.SetProperty(property, propertyValue);
        }

        private void InitProperties()
        {
            // Fanart
            backdrop.GUIImageOne = FanartBackground;
            backdrop.GUIImageTwo = FanartBackground2;
            backdrop.LoadingImage = loadingImage;

            // set context property
            string title = string.IsNullOrEmpty(relatedMovie.Title) ? relatedMovie.IMDbId : relatedMovie.Title;
            GUIUtils.SetProperty("#Trakt.Related.Movie", title);

            // hide watched
            HideWatched = TraktSettings.HideWatchedRelatedMovies;            
            SendingWatchedToTrakt = false;
            if (hideWatchedButton != null)
            {
                GUIControl.SetControlLabel((int)TraktGUIWindows.RelatedMovies, hideWatchedButton.GetID, Translation.HideWatched);
                hideWatchedButton.Selected = HideWatched;
            }

            // no changes yet
            RelationChanged = false;

            // load last layout
            CurrentLayout = (Layout)TraktSettings.RelatedMoviesDefaultLayout;
            // update button label
            GUIControl.SetControlLabel(GetID, layoutButton.GetID, GetLayoutTranslation(CurrentLayout));
        }

        private void ClearProperties()
        {
            GUIUtils.SetProperty("#Trakt.Related.Movie", string.Empty);

            GUIUtils.SetProperty("#Trakt.Movie.Imdb", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Certification", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Overview", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Released", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Runtime", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Tagline", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Title", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Tmdb", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Trailer", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Url", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Year", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.PosterImageFilename", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.FanartImageFilename", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.InCollection", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.InWatchList", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Plays", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Watched", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Rating", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.Icon", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.HatedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.LovedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.Percentage", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.Votes", string.Empty);
        }

        private void PublishMovieSkinProperties(TraktMovie movie)
        {
            SetProperty("#Trakt.Movie.Imdb", movie.Imdb);
            SetProperty("#Trakt.Movie.Certification", movie.Certification);
            SetProperty("#Trakt.Movie.Overview", string.IsNullOrEmpty(movie.Overview) ? Translation.NoMovieSummary : movie.Overview);
            SetProperty("#Trakt.Movie.Released", movie.Released.FromEpoch().ToShortDateString());
            SetProperty("#Trakt.Movie.Runtime", movie.Runtime.ToString());
            SetProperty("#Trakt.Movie.Tagline", movie.Tagline);
            SetProperty("#Trakt.Movie.Title", movie.Title);
            SetProperty("#Trakt.Movie.Tmdb", movie.Tmdb);
            SetProperty("#Trakt.Movie.Trailer", movie.Trailer);
            SetProperty("#Trakt.Movie.Url", movie.Url);
            SetProperty("#Trakt.Movie.Year", movie.Year);
            SetProperty("#Trakt.Movie.PosterImageFilename", movie.Images.PosterImageFilename);
            SetProperty("#Trakt.Movie.FanartImageFilename", movie.Images.FanartImageFilename);
            SetProperty("#Trakt.Movie.InCollection", movie.InCollection.ToString());
            SetProperty("#Trakt.Movie.InWatchList", movie.InWatchList.ToString());
            SetProperty("#Trakt.Movie.Plays", movie.Plays.ToString());
            SetProperty("#Trakt.Movie.Watched", movie.Watched.ToString());
            SetProperty("#Trakt.Movie.Rating", movie.Rating);
            SetProperty("#Trakt.Movie.Ratings.Icon", (movie.Ratings.LovedCount > movie.Ratings.HatedCount) ? "love" : "hate");
            SetProperty("#Trakt.Movie.Ratings.HatedCount", movie.Ratings.HatedCount.ToString());
            SetProperty("#Trakt.Movie.Ratings.LovedCount", movie.Ratings.LovedCount.ToString());
            SetProperty("#Trakt.Movie.Ratings.Percentage", movie.Ratings.Percentage.ToString());
            SetProperty("#Trakt.Movie.Ratings.Votes", movie.Ratings.Votes.ToString());
        }

        private void OnMovieSelected(GUIListItem item, GUIControl parent)
        {
            TraktMovie movie = item.TVTag as TraktMovie;
            PublishMovieSkinProperties(movie);
            GUIImageHandler.LoadFanart(backdrop, movie.Images.FanartImageFilename);
        }

        private void GetImages(List<TraktMovie.MovieImages> itemsWithThumbs)
        {
            StopDownload = false;

            // split the downloads in 5+ groups and do multithreaded downloading
            int groupSize = (int)Math.Max(1, Math.Floor((double)itemsWithThumbs.Count / 5));
            int groups = (int)Math.Ceiling((double)itemsWithThumbs.Count() / groupSize);

            for (int i = 0; i < groups; i++)
            {
                List<TraktMovie.MovieImages> groupList = new List<TraktMovie.MovieImages>();
                for (int j = groupSize * i; j < groupSize * i + (groupSize * (i + 1) > itemsWithThumbs.Count ? itemsWithThumbs.Count - groupSize * i : groupSize); j++)
                {
                    groupList.Add(itemsWithThumbs[j]);
                }

                // sort images so that images that already exist are displayed first
                groupList.Sort((m1, m2) =>
                {
                    int x = Convert.ToInt32(File.Exists(m1.PosterImageFilename)) + Convert.ToInt32(File.Exists(m1.FanartImageFilename));
                    int y = Convert.ToInt32(File.Exists(m2.PosterImageFilename)) + Convert.ToInt32(File.Exists(m2.FanartImageFilename));
                    return y.CompareTo(x);
                });

                new Thread(delegate(object o)
                {
                    List<TraktMovie.MovieImages> items = (List<TraktMovie.MovieImages>)o;
                    foreach (TraktMovie.MovieImages item in items)
                    {
                        #region Poster
                        // stop download if we have exited window
                        if (StopDownload) break;

                        string remoteThumb = item.Poster;
                        string localThumb = item.PosterImageFilename;

                        if (!string.IsNullOrEmpty(remoteThumb) && !string.IsNullOrEmpty(localThumb))
                        {
                            if (GUIImageHandler.DownloadImage(remoteThumb, localThumb))
                            {
                                // notify that image has been downloaded
                                item.NotifyPropertyChanged("PosterImageFilename");
                            }
                        }
                        #endregion

                        #region Fanart
                        // stop download if we have exited window
                        if (StopDownload) break;
                        if (!TraktSettings.DownloadFanart) continue;

                        string remoteFanart = item.Fanart;
                        string localFanart = item.FanartImageFilename;

                        if (!string.IsNullOrEmpty(remoteFanart) && !string.IsNullOrEmpty(localFanart))
                        {
                            if (GUIImageHandler.DownloadImage(remoteFanart, localFanart))
                            {
                                // notify that image has been downloaded
                                item.NotifyPropertyChanged("FanartImageFilename");
                            }
                        }
                        #endregion
                    }
                    #if !MP12
                    // refresh the facade so thumbnails get displayed
                    // this is not needed in MP 1.2 Beta
                    GUIWindowManager.SendThreadMessage(new GUIMessage(GUIMessage.MessageType.GUI_MSG_REFRESH, GUIWindowManager.ActiveWindow, 0, 50, 0, 0, null));
                    #endif
                })
                {
                    IsBackground = true,
                    Name = "Trakt Movie Image Downloader " + i.ToString()
                }.Start(groupList);
            }
        }

        #endregion
    }

    public class GUITraktRelatedMovieListItem : GUIListItem
    {
        public GUITraktRelatedMovieListItem(string strLabel) : base(strLabel) { }

        public object Item
        {
            get { return _Item; }
            set
            {
                _Item = value;
                INotifyPropertyChanged notifier = value as INotifyPropertyChanged;
                if (notifier != null) notifier.PropertyChanged += (s, e) =>
                {
                    if (s is TraktMovie.MovieImages && e.PropertyName == "PosterImageFilename")
                        SetImageToGui((s as TraktMovie.MovieImages).PosterImageFilename);
                    if (s is TraktMovie.MovieImages && e.PropertyName == "FanartImageFilename")
                        UpdateCurrentSelection();
                };
            }
        } protected object _Item;

        /// <summary>
        /// Loads an Image from memory into a facade item
        /// </summary>
        /// <param name="imageFilePath">Filename of image</param>
        protected void SetImageToGui(string imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath)) return;

            // determine the overlay to add to poster
            TraktMovie movie = TVTag as TraktMovie;
            MainOverlayImage mainOverlay = MainOverlayImage.None;

            if (movie.InWatchList)
                mainOverlay = MainOverlayImage.Watchlist;
            else if (movie.Watched)
                mainOverlay = MainOverlayImage.Seenit;

            // add additional overlay if applicable
            if (movie.InCollection)
                mainOverlay |= MainOverlayImage.Library;

            RatingOverlayImage ratingOverlay = RatingOverlayImage.None;

            if (movie.Rating == "love")
                ratingOverlay = RatingOverlayImage.Love;
            else if (movie.Rating == "hate")
                ratingOverlay = RatingOverlayImage.Hate;

            // get a reference to a MediaPortal Texture Identifier
            string suffix = mainOverlay.ToString().Replace(", ", string.Empty) + Enum.GetName(typeof(RatingOverlayImage), ratingOverlay);
            string texture = GUIImageHandler.GetTextureIdentFromFile(imageFilePath, suffix);

            // build memory image
            Image memoryImage = null;
            if (mainOverlay != MainOverlayImage.None || ratingOverlay != RatingOverlayImage.None)
            {
                memoryImage = GUIImageHandler.DrawOverlayOnPoster(imageFilePath, mainOverlay, ratingOverlay);
                if (memoryImage == null) return;

                // load texture into facade item
                if (GUITextureManager.LoadFromMemory(memoryImage, texture, 0, 0, 0) > 0)
                {
                    ThumbnailImage = texture;
                    IconImage = texture;
                    IconImageBig = texture;
                }
            }
            else
            {
                ThumbnailImage = imageFilePath;
                IconImage = imageFilePath;
                IconImageBig = imageFilePath;
            }

            // if selected and is current window force an update of thumbnail
            UpdateCurrentSelection();
        }

        protected void UpdateCurrentSelection()
        {
            GUIRelatedMovies window = GUIWindowManager.GetWindow(GUIWindowManager.ActiveWindow) as GUIRelatedMovies;
            if (window != null)
            {
                GUIListItem selectedItem = GUIControl.GetSelectedListItem((int)TraktGUIWindows.RelatedMovies, 50);
                if (selectedItem == this)
                {
                    GUIWindowManager.SendThreadMessage(new GUIMessage(GUIMessage.MessageType.GUI_MSG_ITEM_SELECT, GUIWindowManager.ActiveWindow, 0, 50, ItemId, 0, null));
                }
            }
        }
    }
}