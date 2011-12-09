﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaPortal.GUI.Library;
using MediaPortal.GUI.Video;
using MediaPortal.Video.Database;

namespace TraktPlugin.GUI
{
    enum TraktGUIWindows
    {
        Main = 87258,
        Calendar = 87259,
        Friends = 87260,
        Recommendations = 87261,
        RecommendationsShows = 87262,
        RecommendationsMovies = 87263,
        Trending = 87264,
        TrendingShows = 87265,
        TrendingMovies = 87266,
        WatchedList = 87267,
        WatchedListShows = 87268,
        WatchedListEpisodes = 87269,
        WatchedListMovies = 87270,
        Settings = 87271,
        SettingsAccount = 87272,
        SettingsPlugins = 87273,
        SettingsGeneral = 87274,
        Lists = 87275,
        ListItems = 87276,
        RelatedMovies = 87277,
        RelatedShows = 87278,
        Shouts = 87280
    }

    enum ExternalPluginWindows
    {
        OnlineVideos = 4755,
        VideoInfo = 2003,
        MovingPictures = 96742,
        TVSeries = 9811,
        MyFilms = 7986,
        MyAnime = 6001,
        MpNZB = 3847,
        MPEISettings = 803,
        MyTorrents = 5678
    }

    enum ExternalPluginControls
    {
        WatchList = 97258,
        Rate = 97259,
        Shouts = 97260,
        CustomList = 97261,
        RelatedItems = 97262
    }

    public class GUICommon
    {
        public static bool CheckLogin()
        {
            return CheckLogin(true);
        }

        /// <summary>
        /// Checks if user is logged in, if not the user is presented with
        /// a choice to jump to Account settings and signup/login.
        /// </summary>
        public static bool CheckLogin(bool showPreviousWindow)
        {
            if (TraktSettings.AccountStatus != TraktAPI.ConnectionState.Connected)
            {
                if (GUIUtils.ShowYesNoDialog(Translation.Login, Translation.NotLoggedIn, true))
                {
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.SettingsAccount);
                    return false;
                }
                if (showPreviousWindow) GUIWindowManager.ShowPreviousWindow();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Checks if a selected movie exists locally and plays movie or
        /// jumps to corresponding plugin details view
        /// </summary>
        /// <param name="jumpTo">false if movie should be played directly</param>
        public static void CheckAndPlayMovie(bool jumpTo, string title, int year, string imdbid)
        {
            bool handled = false;

            if (TraktHelper.IsMovingPicturesAvailableAndEnabled)
            {
                int? movieid = null;

                // Find Movie ID in MovingPictures
                // Movie List is now cached internally in MovingPictures so it will be fast
                bool movieExists = TraktHandlers.MovingPictures.FindMovieID(title, year, imdbid, ref movieid);

                if (movieExists)
                {
                    // Loading Parameter only works in MediaPortal 1.2
                    // Load MovingPictures Details view else, directly play movie if using MP 1.1
                    #if MP12
                    if (jumpTo)
                    {
                        string loadingParameter = string.Format("movieid:{0}", movieid);
                        // Open MovingPictures Details view so user can play movie
                        GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MovingPictures, loadingParameter);
                    }
                    else
                        TraktHandlers.MovingPictures.PlayMovie(movieid);
                    #else
                    TraktHandlers.MovingPictures.PlayMovie(movieid);
                    #endif
                    handled = true;
                }
            }

            // check if its in My Videos database
            if (TraktSettings.MyVideos >= 0 && handled == false)
            {
                IMDBMovie movie = null;
                if (TraktHandlers.MyVideos.FindMovieID(title, year, imdbid, ref movie))
                {
                    // Open My Videos Video Info view so user can play movie
                    if (jumpTo)
                    {
                        GUIVideoInfo videoInfo = (GUIVideoInfo)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_VIDEO_INFO);
                        videoInfo.Movie = movie;
                        GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_VIDEO_INFO);
                    }
                    else
                    {
                        GUIVideoFiles.PlayMovie(movie.ID);
                    }
                    handled = true;
                }
            }

            // check if its in My Films database
            #if MP12
            if (TraktHelper.IsMyFilmsAvailableAndEnabled && handled == false)
            {
                int? movieid = null;
                string config = null;
                if (TraktHandlers.MyFilmsHandler.FindMovie(title, year, imdbid, ref movieid, ref config))
                {
                    // Open My Films Details view so user can play movie
                    if (jumpTo)
                    {
                        string loadingParameter = string.Format("config:{0}|movieid:{1}", config, movieid);
                        GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyFilms, loadingParameter);
                    }
                    else
                    {
                        // TraktHandlers.MyFilms.PlayMovie(config, movieid); // ToDo: Add Player Class to MyFilms
                        string loadingParameter = string.Format("config:{0}|movieid:{1}|play:{2}", config, movieid, "true");
                        GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyFilms, loadingParameter);
                    }
                    handled = true;
                }
            }
            #endif

            #if MP12
            if (TraktHelper.IsOnlineVideosAvailableAndEnabled && handled == false)
            {
                string loadingParameter = string.Format("site:IMDb Movie Trailers|search:{0}|return:Locked", imdbid);
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParameter);
                handled = true;
            }
            #endif

        }

        /// <summary>
        /// Checks if a selected episode exists locally and plays episode
        /// </summary>
        /// <param name="seriesid">the series tvdb id of episode</param>
        /// <param name="imdbid">the series imdb id of episode</param>
        /// <param name="seasonidx">the season index of episode</param>
        /// <param name="episodeidx">the episode index of episode</param>
        public static void CheckAndPlayEpisode(int seriesid, string imdbid, int seasonidx, int episodeidx)
        {
            bool handled = false;

            // check if plugin is installed and enabled
            if (TraktHelper.IsMPTVSeriesAvailableAndEnabled)
            {
                // Play episode if it exists
                handled = TraktHandlers.TVSeries.PlayEpisode(seriesid, seasonidx, episodeidx);
            }

            if (TraktHelper.IsMyAnimeAvailableAndEnabled && handled == false)
            {
                handled = TraktHandlers.MyAnime.PlayEpisode(seriesid, seasonidx, episodeidx);
            }

            #if MP12
            if (TraktHelper.IsOnlineVideosAvailableAndEnabled && handled == false)
            {
                TraktLogger.Info("No episodes found! Attempting Trailer lookup in IMDb Trailers.");
                string loadingParameter = string.Format("site:IMDb Movie Trailers|search:{0}|return:Locked", imdbid);
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParameter);
                handled = true;
            }
            #endif
        }

        /// <summary>
        /// Checks if a selected show exists locally and plays first unwatched episode
        /// </summary>
        /// <param name="seriesid">the series tvdb id of show</param>
        /// <param name="imdbid">the series imdb id of show</param>
        public static void CheckAndPlayFirstUnwatched(int seriesid, string imdbid)
        {
            TraktLogger.Info("Attempting to play TVDb: {0}, IMDb: {1}", seriesid.ToString(), imdbid);
            bool handled = false;

            // check if plugin is installed and enabled
            if (TraktHelper.IsMPTVSeriesAvailableAndEnabled)
            {
                // Play episode if it exists
                TraktLogger.Info("Checking if any episodes to watch in MP-TVSeries");
                handled = TraktHandlers.TVSeries.PlayFirstUnwatchedEpisode(seriesid);
            }

            if (TraktHelper.IsMyAnimeAvailableAndEnabled && handled == false)
            {
                TraktLogger.Info("Checking if any episodes to watch in My Anime");
                handled = TraktHandlers.MyAnime.PlayFirstUnwatchedEpisode(seriesid);
            }

            #if MP12
            if (TraktHelper.IsOnlineVideosAvailableAndEnabled && handled == false)
            {
                TraktLogger.Info("No episodes found! Attempting Trailer lookup in IMDb Trailers.");
                string loadingParameter = string.Format("site:IMDb Movie Trailers|search:{0}|return:Locked", imdbid);
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParameter);
                handled = true;
            }
            #endif
        }
    }
}
