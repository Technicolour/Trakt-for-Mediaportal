﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using TraktAPI.DataStructures;
using TraktAPI.Enums;
using TraktAPI.Extensions;

namespace TraktAPI
{
    public static class TraktAPI
    {
        #region Web Events

        // these events can be used to log data sent / received from trakt
        public delegate void OnDataSendDelegate(string url, string postData);
        public delegate void OnDataReceivedDelegate(string response, HttpWebResponse webResponse);
        public delegate void OnDataErrorDelegate(string error);
        public delegate void OnLatencyDelegate(double totalElapsedTime, HttpWebResponse webResponse, int dataSent, int dataReceived);

        public static event OnDataSendDelegate OnDataSend;
        public static event OnDataReceivedDelegate OnDataReceived;
        public static event OnDataErrorDelegate OnDataError;
        public static event OnLatencyDelegate OnLatency;

        #endregion

        #region Settings

        /// <summary>
        /// ClientId should be set before using the API and is specific to the application
        /// </summary>
        public static string ClientId { get; set; }
        /// <summary>
        /// ClientSecret should be set before using the API and is specific to the application
        /// </summary>        
        public static string ClientSecret { get; set; }
        /// <summary>
        /// RedirectUri should be set before using the API and is specific to the application
        /// </summary>
        public static string RedirectUri { get; set; }

        /// <summary>
        /// UserAccessToken is set once we authorise the application
        /// The application using the API should persist this as well as the refresh token
        /// </summary>
        public static string UserAccessToken { get; set; }
        
        public static string UserAgent { get; set; }
        public static bool UseSSL { get; set; }
        /// <summary>
        /// Set this when acess token polling should halt
        /// </summary>
        public static bool AuthorisationCancelled { get; set; }

        #endregion

        #region Trakt Methods

        #region Authentication

        /// <summary>
        /// View Documentation to understand the the flow @
        /// http://docs.trakt.apiary.io/#reference/authentication-devices/authorize-application
        /// </summary>
        
        public static TraktDeviceCode GetDeviceCode()
        {
            var response = PostToTrakt(TraktURIs.DeviceCode, new TraktClientId { ClientId = ClientId }.ToJSON());
            return response.FromJSON<TraktDeviceCode>();
        }

        public static TraktAuthenticationToken GetAuthenticationToken(TraktDeviceCode code)
        {
            if (code == null) return null;

            var clientCode = new TraktClientCode
            {
                Code = code.DeviceCode,
                ClientId = ClientId,
                ClientSecret = ClientSecret
            };
            
            int pollCounter = 0;

            do
            {
                if (AuthorisationCancelled) return null;

                var response = PostToTrakt(TraktURIs.AccessToken, clientCode.ToJSON()).FromJSON<TraktAuthenticationToken>();

                if (response == null || AuthorisationCancelled) return null;

                // check the return code on the request to see if we should contine polling
                // http://docs.trakt.apiary.io/#reference/authentication-devices/get-token/generate-new-device-codes                                
                // 400 : Pending
                // 404 : Not Found
                // 409 : Already Used
                // 410 : Expired
                // 418 : Denied
                // 429 : Slow Down
                if (response.Code != 0 && response.Code != 200)
                {
                    switch (response.Code)
                    {
                        case 404:
                        case 409:
                        case 410:
                        case 418:
                            // fatal, we can't continue
                            return null;
                        default:
                            break;
                    }
                }
                else if (response.AccessToken != null)
                {
                    UserAccessToken = response.AccessToken;
                    return response;
                }

                // sleep the required time (interval) before checking again
                // otherwise will return a 429 (slow down) error
                Thread.Sleep(1000 * code.Interval);
            }
            while ((++pollCounter * code.Interval) < code.ExpiresIn);

            return null;
        }
        
        /// <summary>
        /// This should be called approx 3 months after successfully retrieving the access token
        /// </summary> 
        public static TraktAuthenticationToken RefreshAccessToken(string token)
        {
            var refreshToken = new TraktRefreshToken
            {
                RefreshToken = token,
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                RedirectUri = RedirectUri,
                GrantType = "refresh_token"
            };

            var response = PostToTrakt(TraktURIs.RefreshToken, refreshToken.ToJSON());
            return response.FromJSON<TraktAuthenticationToken>();
        }

        public static void RevokeToken()
        {
            // note: this method does not use JSON!
            PostToTrakt(TraktURIs.RevokeToken, 
                        string.Format("token={0}", UserAccessToken), 
                        true, 
                        "POST", 
                        "application/x-www-form-urlencoded");
        }

        #endregion

        #region Sync

        public static TraktLastSyncActivities GetLastSyncActivities()
        {
            var response = GetFromTrakt(TraktURIs.SyncLastActivities);
            return response.FromJSON<TraktLastSyncActivities>();
        }

        #endregion

        #region Playback

        public static IEnumerable<TraktSyncPausedMovie> GetPausedMovies()
        {
            var response = GetFromTrakt(TraktURIs.SyncPausedMovies);
            return response.FromJSONArray<TraktSyncPausedMovie>();
        }

        public static IEnumerable<TraktSyncPausedEpisode> GetPausedEpisodes()
        {
            var response = GetFromTrakt(TraktURIs.SyncPausedEpisodes);
            return response.FromJSONArray<TraktSyncPausedEpisode>();
        }

        #endregion

        #region Collection

        public static IEnumerable<TraktMovieCollected> GetCollectedMovies()
        {
            var response = GetFromTrakt(TraktURIs.SyncCollectionMovies);
            return response.FromJSONArray<TraktMovieCollected>();
        }

        public static IEnumerable<TraktEpisodeCollected> GetCollectedEpisodes()
        {
            var response = GetFromTrakt(TraktURIs.SyncCollectionEpisodes);
            return response.FromJSONArray<TraktEpisodeCollected>();
        }

        #endregion

        #region Watched History

        public static IEnumerable<TraktMovieWatched> GetWatchedMovies()
        {
            var response = GetFromTrakt(TraktURIs.SyncWatchedMovies);
            return response.FromJSONArray<TraktMovieWatched>();
        }

        public static IEnumerable<TraktEpisodeWatched> GetWatchedEpisodes()
        {
            var response = GetFromTrakt(TraktURIs.SyncWatchedEpisodes);
            return response.FromJSONArray<TraktEpisodeWatched>();
        }

        #endregion

        #region Ratings

        public static IEnumerable<TraktMovieRated> GetRatedMovies()
        {
            var response = GetFromTrakt(TraktURIs.SyncRatedMovies);
            return response.FromJSONArray<TraktMovieRated>();
        }

        public static IEnumerable<TraktEpisodeRated> GetRatedEpisodes()
        {
            var response = GetFromTrakt(TraktURIs.SyncRatedEpisodes);
            return response.FromJSONArray<TraktEpisodeRated>();
        }

        public static IEnumerable<TraktShowRated> GetRatedShows()
        {
            var response = GetFromTrakt(TraktURIs.SyncRatedShows);
            return response.FromJSONArray<TraktShowRated>();
        }

        public static IEnumerable<TraktSeasonRated> GetRatedSeasons()
        {
            var response = GetFromTrakt(TraktURIs.SyncRatedSeasons);
            return response.FromJSONArray<TraktSeasonRated>();
        }

        #endregion

        #region User

        public static TraktSettings GetUserSettings()
        {
            WebHeaderCollection headerCollection = null;
            var response = GetFromTrakt(TraktURIs.UserSettings, out headerCollection, "GET", true);
            return response.FromJSON<TraktSettings>();
        }
            
        public static TraktUserStatistics GetUserStatistics(string username = "me")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserStats, username));
            return response.FromJSON<TraktUserStatistics>();
        }

        public static TraktUserSummary GetUserProfile(string username = "me")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserProfile, username));
            return response.FromJSON<TraktUserSummary>();
        }

        /// <summary>
        /// Gets a list of follower requests for the current user
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<TraktFollowerRequest> GetFollowerRequests()
        {
            var response = GetFromTrakt(TraktURIs.UserFollowerRequests);
            return response.FromJSONArray<TraktFollowerRequest>();
        }

        /// <summary>
        /// Returns a list of Friends for current user
        /// Friends are a two-way relationship ie. both following each other
        /// </summary>
        public static IEnumerable<TraktNetworkFriend> GetNetworkFriends()
        {
            return GetNetworkFriends("me");
        }
        public static IEnumerable<TraktNetworkFriend> GetNetworkFriends(string username)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.NetworkFriends, username));
            return response.FromJSONArray<TraktNetworkFriend>();
        }

        /// <summary>
        /// Returns a list of people the current user follows
        /// </summary>
        public static IEnumerable<TraktNetworkUser> GetNetworkFollowing()
        {
            return GetNetworkFollowing("me");
        }
        public static IEnumerable<TraktNetworkUser> GetNetworkFollowing(string username)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.NetworkFollowing, username));
            return response.FromJSONArray<TraktNetworkUser>();
        }

        /// <summary>
        /// Returns a list of people that follow the current user
        /// </summary>
        public static IEnumerable<TraktNetworkUser> GetNetworkFollowers()
        {
            return GetNetworkFollowers("me");
        }
        public static IEnumerable<TraktNetworkUser> GetNetworkFollowers(string username)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.NetworkFollowers, username));
            return response.FromJSONArray<TraktNetworkUser>();
        }

        public static TraktNetworkUser NetworkApproveFollower(int id)
        {
            string response = PostToTrakt(string.Format(TraktURIs.NetworkFollowRequest, id), string.Empty);
            return response.FromJSON<TraktNetworkUser>();
        }

        public static bool NetworkDenyFollower(int id)
        {
            return DeleteFromTrakt(string.Format(TraktURIs.NetworkFollowRequest, id));
        }

        public static TraktNetworkApproval NetworkFollowUser(string username)
        {
            string response = PostToTrakt(string.Format(TraktURIs.NetworkFollowUser, username), string.Empty);
            return response.FromJSON<TraktNetworkApproval>();
        }

        public static bool NetworkUnFollowUser(string username)
        {
            return DeleteFromTrakt(string.Format(TraktURIs.NetworkFollowUser, username));
        }

        public static IEnumerable<TraktMovieHistory> GetUsersMovieWatchedHistory(string username = "me", int page = 1, int maxItems = 100)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserWatchedHistoryMovies, username, page, maxItems));
            return response.FromJSONArray<TraktMovieHistory>();
        }

        public static IEnumerable<TraktEpisodeHistory> GetUsersEpisodeWatchedHistory(string username = "me", int page = 1, int maxItems = 100)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserWatchedHistoryEpisodes, username, page, maxItems));
            return response.FromJSONArray<TraktEpisodeHistory>();
        }

        /// <summary>
        /// Get comments for user sorted by most recent
        /// </summary>
        /// <param name="username">Username of person that made comment</param>
        /// <param name="commentType">all, reviews, shouts</param>
        /// <param name="type"> all, movies, shows, seasons, episodes, lists</param>
        /// <param name="extendedInfoParams">Extended Info: min, full, images (comma separated)</param>
        public static TraktComments GetUsersComments(string username = "me", string commentType = "all", string type = "all", string extendedInfoParams = "min", int page = 1, int maxItems = 10)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.UserComments, username, commentType, type, extendedInfoParams, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktComments
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    Comments = response.FromJSONArray<TraktCommentItem>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Hidden Items

        /// <summary>
        /// Get hidden items for a section
        /// </summary>
        /// <param name="section">Possible values: calendar, progress_watched, progress_collected, recommendations
        /// <param name="type">Narrow down by element type: movie, show, season</param>
        /// <param name="extendedInfoParams">Extended Info: min, full, images (comma separated)</param>
        public static TraktHiddenItems GetHiddenItems(string section, string type, string extendedInfoParams = "min", int page = 1, int maxItems = 10)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.UserHiddenItems, section, type, extendedInfoParams, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                // add the section to the results
                var hiddenItems = response.FromJSONArray<TraktHiddenItem>().ToNullableList();
                hiddenItems.ForEach(h => h.Section = section);

                return new TraktHiddenItems
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    HiddenItems = hiddenItems
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        /// <summary>
        /// Hide items for a specific section
        /// </summary>
        /// <param name="section">Possible values: calendar, progress_watched, progress_collected, recommendations
        /// <param name="hiddenItems">List of items to hide</param>
        public static TraktSyncResponse AddHiddenItems(string section, TraktSyncHiddenItems hiddenItems)
        {
            var response = PostToTrakt(string.Format(TraktURIs.UserHiddenItemAdd, section), hiddenItems.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        /// <summary>
        /// Unhide items for a specific section
        /// </summary>
        /// <param name="section">Possible values: calendar, progress_watched, progress_collected, recommendations
        /// <param name="hiddenItems">List of items to unhide</param>
        public static TraktSyncResponse RemoveHiddenItems(string section, TraktSyncHiddenItems hiddenItems)
        {
            var response = PostToTrakt(string.Format(TraktURIs.UserHiddenItemRemove, section), hiddenItems.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        #region Single Object Handlers

        public static TraktSyncResponse AddMovieToHiddenItems(TraktMovie movie, string section)
        {
            var movies = new TraktSyncHiddenItems
            {
                Movies = new List<TraktMovie>() { movie }
            };

            return AddHiddenItems(section, movies);
        }

        public static TraktSyncResponse AddShowToHiddenItems(TraktShow show, string section)
        {
            var shows = new TraktSyncHiddenItems
            {
                Shows = new List<TraktShow>() { show }
            };

            return AddHiddenItems(section, shows);
        }

        public static TraktSyncResponse AddSeasonToHiddenItems(TraktSeason season, string section)
        {
            var seasons = new TraktSyncHiddenItems
            {
                Seasons = new List<TraktSeason>() { season }
            };

            return AddHiddenItems(section, seasons);
        }

        public static TraktSyncResponse RemoveMovieFromHiddenItems(TraktMovie movie, string section)
        {
            var movies = new TraktSyncHiddenItems
            {
                Movies = new List<TraktMovie>() { movie }
            };

            return RemoveHiddenItems(section, movies);
        }

        public static TraktSyncResponse RemoveShowFromHiddenItems(TraktShow show, string section)
        {
            var shows = new TraktSyncHiddenItems
            {
                Shows = new List<TraktShow>() { show }
            };

            return RemoveHiddenItems(section, shows);
        }

        public static TraktSyncResponse RemoveSeasonFromHiddenItems(TraktSeason season, string section)
        {
            var seasons = new TraktSyncHiddenItems
            {
                Seasons = new List<TraktSeason>() { season }
            };

            return RemoveHiddenItems(section, seasons);
        }

        #endregion

        #endregion

        #region Lists

        public static TraktListsTrending GetTrendingLists(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.TrendingLists, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktListsTrending
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),                    
                    Lists = response.FromJSONArray<TraktListTrending>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        public static TraktListsPopular GetPopularLists(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.PopularLists, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktListsPopular
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    Lists = response.FromJSONArray<TraktListPopular>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        public static IEnumerable<TraktListDetail> GetUserLists(string username = "me")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserLists, username));
            return response.FromJSONArray<TraktListDetail>();
        }

        public static IEnumerable<TraktListItem> GetUserListItems(string username, string listId, string extendedInfoParams = "min")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserListItems, username, listId, extendedInfoParams));
            return response.FromJSONArray<TraktListItem>();
        }

        public static TraktListDetail CreateCustomList(TraktList list, string username = "me")
        {
            var response = PostToTrakt(string.Format(TraktURIs.UserListAdd, username), list.ToJSON());
            return response.FromJSON<TraktListDetail>();
        }

        public static TraktListDetail UpdateCustomList(TraktListDetail list, string username = "me")
        {
            var response = ReplaceOnTrakt(string.Format(TraktURIs.UserListEdit, username), list.ToJSON());
            return response.FromJSON<TraktListDetail>();
        }

        public static TraktSyncResponse AddItemsToList(string username, string id, TraktSyncAll items)
        {
            var response = PostToTrakt(string.Format(TraktURIs.UserListItemsAdd, username, id), items.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveItemsFromList(string username, string id, TraktSyncAll items)
        {
            var response = PostToTrakt(string.Format(TraktURIs.UserListItemsRemove, username, id), items.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static bool DeleteUserList(string username, string listId)
        {
            return DeleteFromTrakt(string.Format(TraktURIs.DeleteList, username, listId));
        }

        public static bool LikeList(string username, int id)
        {
            var response = PostToTrakt(string.Format(TraktURIs.UserListLike, username,id), null);
            return response != null;
        }

        public static bool UnLikeList(string username, int id)
        {
            return DeleteFromTrakt(string.Format(TraktURIs.UserListLike, username, id));
        }

        public static IEnumerable<TraktComment> GetUserListComments(string username, string id, string sortMethod = "newest", int page = 1, int maxItems = 1000)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserListComments, username, id, sortMethod, page, maxItems));
            return response.FromJSONArray<TraktComment>();
        }

        #endregion

        #region Watchlists

        public static IEnumerable<TraktMovieWatchList> GetWatchListMovies(string username = "me", string extendedInfoParams = "min")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserWatchlistMovies, username, extendedInfoParams));
            return response.FromJSONArray<TraktMovieWatchList>();
        }

        public static IEnumerable<TraktShowWatchList> GetWatchListShows(string username = "me", string extendedInfoParams = "min")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserWatchlistShows, username, extendedInfoParams));
            return response.FromJSONArray<TraktShowWatchList>();
        }

        public static IEnumerable<TraktSeasonWatchList> GetWatchListSeasons(string username = "me", string extendedInfoParams = "min")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserWatchlistSeasons, username, extendedInfoParams));
            return response.FromJSONArray<TraktSeasonWatchList>();
        }

        public static IEnumerable<TraktEpisodeWatchList> GetWatchListEpisodes(string username = "me", string extendedInfoParams = "min")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.UserWatchlistEpisodes, username, extendedInfoParams));
            return response.FromJSONArray<TraktEpisodeWatchList>();
        }

        #endregion

        #region Likes

        /// <summary>
        /// Gets the current users liked items (comments and/or lists)
        /// </summary>
        /// <param name="type">The type of liked item: all (default), lists or comments</param>
        /// <param name="extendedInfoParams">Extended Info: min, full, images (comma separated)</param>
        /// <param name="page">Page Number</param>
        /// <param name="maxItems">Maximum number of items to request per page (this should be consistent per page request)</param>
        public static TraktLikes GetLikedItems(string type = "all", string extendedInfoParams = "min", int page = 1, int maxItems = 10)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.UserLikedItems, type, extendedInfoParams, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktLikes
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    Likes = response.FromJSONArray<TraktLike>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Movies

        #region Box Office

        public static IEnumerable<TraktMovieBoxOffice> GetBoxOffice()
        {
            var response = GetFromTrakt(TraktURIs.BoxOffice);
            return response.FromJSONArray<TraktMovieBoxOffice>();
        }

        #endregion

        #region Related

        public static IEnumerable<TraktMovieSummary> GetRelatedMovies(string id, int limit = 10)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.RelatedMovies, id, limit));
            return response.FromJSONArray<TraktMovieSummary>();
        }

        #endregion

        #region Comments

        public static IEnumerable<TraktComment> GetMovieComments(string id, int page = 1, int maxItems = 1000)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.MovieComments, id, page, maxItems));
            return response.FromJSONArray<TraktComment>();
        }

        #endregion

        #region Popular

        public static TraktMoviesPopular GetPopularMovies(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.PopularMovies, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktMoviesPopular
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    Movies = response.FromJSONArray<TraktMovieSummary>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Trending

        public static TraktMoviesTrending GetTrendingMovies(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.TrendingMovies, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktMoviesTrending
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    TotalWatchers = int.Parse(headers["X-Trending-User-Count"]),
                    Movies = response.FromJSONArray<TraktMovieTrending>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Anticipated

        public static TraktMoviesAnticipated GetAnticipatedMovies(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.AnticipatedMovies, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktMoviesAnticipated
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    Movies = response.FromJSONArray<TraktMovieAnticipated>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Recommendations

        public static IEnumerable<TraktMovieSummary> GetRecommendedMovies(string extendedInfoParams = "min")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.RecommendedMovies, extendedInfoParams));
            return response.FromJSONArray<TraktMovieSummary>();
        }

        public static bool DismissRecommendedMovie(string movieId)
        {
            return DeleteFromTrakt(string.Format(TraktURIs.DismissRecommendedMovie, movieId));
        }

        #endregion

        #region Updates

        public static TraktMoviesUpdated GetRecentlyUpdatedMovies(string sincedate, int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.MovieUpdates, sincedate, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktMoviesUpdated
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    Movies = response.FromJSONArray<TraktMovieUpdate>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Summary

        public static TraktMovieSummary GetMovieSummary(string id)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.MovieSummary, id));
            return response.FromJSON<TraktMovieSummary>();
        }

        #endregion

        #region People

        public static TraktCredits GetMoviePeople(string id)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.MoviePeople, id));
            return response.FromJSON<TraktCredits>();
        }

        #endregion

        #endregion

        #region Shows

        #region Related

        public static IEnumerable<TraktShowSummary> GetRelatedShows(string id, int limit = 10)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.RelatedShows, id, limit));
            return response.FromJSONArray<TraktShowSummary>();
        }

        #endregion

        #region Summary

        public static TraktShowSummary GetShowSummary(string id)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.ShowSummary, id));
            return response.FromJSON<TraktShowSummary>();
        }

        #endregion

        #region Updates

        public static TraktShowsUpdated GetRecentlyUpdatedShows(string sincedate, int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.ShowUpdates, sincedate, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktShowsUpdated
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    Shows = response.FromJSONArray<TraktShowUpdate>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Seasons

        /// <summary>
        /// Gets the seasons for a show
        /// </summary>
        /// <param name="id">the id of the tv show</param>
        /// <param name="extendedParameter">request parameters, "episodes,full"</param>
        public static IEnumerable<TraktSeasonSummary> GetShowSeasons(string id, string extendedParameter = "full")
        {
            var response = GetFromTrakt(string.Format(TraktURIs.ShowSeasons, id, extendedParameter));
            return response.FromJSONArray<TraktSeasonSummary>();
        }

        public static IEnumerable<TraktComment> GetSeasonComments(string id, int season, int page = 1, int maxItems = 1000)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.SeasonComments, id, season, page, maxItems));
            return response.FromJSONArray<TraktComment>();
        }

        #endregion

        #region Comments

        public static IEnumerable<TraktComment> GetShowComments(string id, int page = 1, int maxItems = 1000)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.ShowComments, id, page, maxItems));
            return response.FromJSONArray<TraktComment>();
        }

        #endregion

        #region Popular

        public static TraktShowsPopular GetPopularShows(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.PopularShows, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktShowsPopular
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    Shows = response.FromJSONArray<TraktShowSummary>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Anticipated

        public static TraktShowsAnticipated GetAnticipatedShows(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.AnticipatedShows, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktShowsAnticipated
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    Shows = response.FromJSONArray<TraktShowAnticipated>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Trending

        public static TraktShowsTrending GetTrendingShows(int page = 1, int maxItems = 100)
        {
            var headers = new WebHeaderCollection();

            var response = GetFromTrakt(string.Format(TraktURIs.TrendingShows, page, maxItems), out headers);
            if (response == null)
                return null;

            try
            {
                return new TraktShowsTrending
                {
                    CurrentPage = page,
                    TotalItemsPerPage = maxItems,
                    TotalPages = int.Parse(headers["X-Pagination-Page-Count"]),
                    TotalItems = int.Parse(headers["X-Pagination-Item-Count"]),
                    TotalWatchers = int.Parse(headers["X-Trending-User-Count"]),
                    Shows = response.FromJSONArray<TraktShowTrending>()
                };
            }
            catch
            {
                // most likely bad header response
                return null;
            }
        }

        #endregion

        #region Recommendations

        public static IEnumerable<TraktShowSummary> GetRecommendedShows()
        {
            var response = GetFromTrakt(TraktURIs.RecommendedShows);
            return response.FromJSONArray<TraktShowSummary>();
        }

        public static bool DismissRecommendedShow(string showId)
        {
            return DeleteFromTrakt(string.Format(TraktURIs.DismissRecommendedShow, showId));
        }

        #endregion

        #region TV Calendar
        
        /// <summary>
        /// Returns list of episodes in the users Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktShowCalendar> GetCalendarUserShows(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarMyShows, startDate, days), "GET");
            return calendar.FromJSONArray<TraktShowCalendar>();
        }

        /// <summary>
        /// Returns list of new episodes in the users Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktShowCalendar> GetCalendarUserNewShows(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarMyNewShows, startDate, days), "GET");
            return calendar.FromJSONArray<TraktShowCalendar>();
        }

        /// <summary>
        /// Returns list of season premiere episodes in the users Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktShowCalendar> GetCalendarUserSeasonPremieresShows(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarMySeasonPremieresShows, startDate, days), "GET");
            return calendar.FromJSONArray<TraktShowCalendar>();
        }
        
        /// <summary>
        /// Returns list of all episodes in the Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktShowCalendar> GetCalendarShows(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarAllShows, startDate, days), "GET");
            return calendar.FromJSONArray<TraktShowCalendar>();
        }

        /// <summary>
        /// Returns list of all new episodes in the Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktShowCalendar> GetCalendarNewShows(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarAllNewShows, startDate, days), "GET");
            return calendar.FromJSONArray<TraktShowCalendar>();
        }

        /// <summary>
        /// Returns list of all season premiere episodes in the Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktShowCalendar> GetCalendarSeasonPremieresShows(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarAllSeasonPremieresShows, startDate, days), "GET");
            return calendar.FromJSONArray<TraktShowCalendar>();
        }

        #endregion

        #region Movie Calendar

        /// <summary>
        /// Returns list of movies in the users Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktMovieCalendar> GetCalendarUserMovies(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarMyMovies, startDate, days), "GET");
            return calendar.FromJSONArray<TraktMovieCalendar>();
        }

        /// <summary>
        /// Returns list of DVDs/ Blurays in the users Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktMovieCalendar> GetCalendarUserDVDs(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarMyDVDs, startDate, days), "GET");
            return calendar.FromJSONArray<TraktMovieCalendar>();
        }

        /// <summary>
        /// Returns list of all movies in the Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktMovieCalendar> GetCalendarMovies(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarAllMovies, startDate, days), "GET");
            return calendar.FromJSONArray<TraktMovieCalendar>();
        }

        /// <summary>
        /// Returns list of all DVDs/ Blurays in the Calendar
        /// </summary>
        /// <param name="startDate">Start Date of calendar in the form yyyy-MM-dd</param>
        /// <param name="days">Number of days to return in calendar, maximum days allowed is 31</param>
        public static IEnumerable<TraktMovieCalendar> GetCalendarDVDs(string startDate, int days = 7)
        {
            string calendar = GetFromTrakt(string.Format(TraktURIs.CalendarAllDVDs, startDate, days), "GET");
            return calendar.FromJSONArray<TraktMovieCalendar>();
        }

        #endregion

        #region People

        public static TraktShowCredits GetShowPeople(string id)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.ShowPeople, id));
            return response.FromJSON<TraktShowCredits>();
        }

        #endregion

        #endregion

        #region Episodes

        #region Comments

        public static IEnumerable<TraktComment> GetEpisodeComments(string id, int season, int episode, int page = 1, int maxItems = 1000)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.EpisodeComments, id, season, episode, page, maxItems));
            return response.FromJSONArray<TraktComment>();
        }

        #endregion

        #region Season Episodes

        public static IEnumerable<TraktEpisodeSummary> GetSeasonEpisodes(string showId, string seasonId)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.SeasonEpisodes, showId, seasonId));
            return response.FromJSONArray<TraktEpisodeSummary>();
        }

        #endregion

        #endregion
        
        #region Search
        
        /// <summary>
        /// Search from one or more types, movies, episodes, shows etc...
        /// </summary>
        /// <param name="searchTerm">string to search for</param>
        /// <param name="types">a list of search types</param>
        /// <returns>returns results from multiple search types</returns>
        public static IEnumerable<TraktSearchResult> Search(string searchTerm, HashSet<SearchType> types, int maxResults)
        {
            // get a comma seperated list of types to search on
            string joinedTypes = string.Join(",", types);

            string response = GetFromTrakt(string.Format(TraktURIs.SearchAll, joinedTypes, HttpUtility.UrlEncode(searchTerm), 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        /// <summary>
        /// Returns a list of users found using search term
        /// </summary>
        public static IEnumerable<TraktSearchResult> SearchForUsers(string searchTerm)
        {
            return SearchUsers(searchTerm, 30);
        }
        public static IEnumerable<TraktSearchResult> SearchUsers(string searchTerm, int maxResults)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.SearchUsers, HttpUtility.UrlEncode(searchTerm), 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        /// <summary>
        /// Returns a list of movies found using search term
        /// </summary>
        public static IEnumerable<TraktSearchResult> SearchMovies(string searchTerm)
        {
            return SearchMovies(searchTerm, 30);
        }
        public static IEnumerable<TraktSearchResult> SearchMovies(string searchTerm, int maxResults)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.SearchMovies, HttpUtility.UrlEncode(searchTerm), 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        /// <summary>
        /// Returns a list of shows found using search term
        /// </summary>
        public static IEnumerable<TraktSearchResult> SearchShows(string searchTerm)
        {
            return SearchShows(searchTerm, 30);
        }
        public static IEnumerable<TraktSearchResult> SearchShows(string searchTerm, int maxResults)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.SearchShows, HttpUtility.UrlEncode(searchTerm), 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        /// <summary>
        /// Returns a list of episodes found using search term
        /// </summary>
        public static IEnumerable<TraktSearchResult> SearchEpisodes(string searchTerm)
        {
            return SearchEpisodes(searchTerm, 30);
        }
        public static IEnumerable<TraktSearchResult> SearchEpisodes(string searchTerm, int maxResults)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.SearchEpisodes, HttpUtility.UrlEncode(searchTerm), 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        /// <summary>
        /// Returns a list of people found using search term
        /// </summary>
        public static IEnumerable<TraktSearchResult> SearchPeople(string searchTerm)
        {
            return SearchPeople(searchTerm, 30);
        }
        public static IEnumerable<TraktSearchResult> SearchPeople(string searchTerm, int maxResults)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.SearchPeople, HttpUtility.UrlEncode(searchTerm), 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        /// <summary>
        /// Returns a list of lists found using search term
        /// </summary>
        public static IEnumerable<TraktSearchResult> SearchLists(string searchTerm)
        {
            return SearchLists(searchTerm, 30);
        }
        public static IEnumerable<TraktSearchResult> SearchLists(string searchTerm, int maxResults)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.SearchLists, HttpUtility.UrlEncode(searchTerm), 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        /// <summary>
        /// Returns a list of items found when searching by id
        /// </summary>
        /// <param name="type">trakt, imdb, tmdb, tvdb, tvrage</param>
        /// <param name="id">the id to search by e.g. tt0848228, may not be unique for some id types</param>
        /// <param name="idType">the object type e.g. movie, show, episode, person, this will prevent duplicates of the same id</param>
        /// <param name="maxResults">maximum number of results to return, defaults to 30</param>
        public static IEnumerable<TraktSearchResult> SearchById(string type, string id, string idType, int maxResults = 30)
        {
            string response = GetFromTrakt(string.Format(TraktURIs.SearchById, type, id, idType, 1, maxResults));
            return response.FromJSONArray<TraktSearchResult>();
        }

        #endregion

        #region Collection

        public static TraktSyncResponse AddMoviesToCollecton(TraktSyncMoviesCollected movies)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionAdd, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveMoviesFromCollecton(TraktSyncMovies movies)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionRemove, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToCollectonEx(TraktSyncShowsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToCollecton(TraktSyncShows shows)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveShowsFromCollecton(TraktSyncShows shows)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddEpisodesToCollecton(TraktSyncEpisodesCollected episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionAdd, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveEpisodesFromCollecton(TraktSyncEpisodes episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionRemove, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToCollectonEx(TraktSyncShowsCollectedEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }
        
        public static TraktSyncResponse RemoveShowsFromCollectonEx(TraktSyncShowsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncCollectionRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        #endregion

        #region Collection (Single)

        public static TraktSyncResponse AddMovieToCollection(TraktSyncMovieCollected movie)
        {
            var movies = new TraktSyncMoviesCollected
            {
                Movies = new List<TraktSyncMovieCollected>() { movie }
            };

            return AddMoviesToCollecton(movies);
        }

        public static TraktSyncResponse RemoveMovieFromCollection(TraktMovie movie)
        {
            var movies = new TraktSyncMovies
            {
                Movies = new List<TraktMovie>() { movie }
            };

            return RemoveMoviesFromCollecton(movies);
        }

        public static TraktSyncResponse AddShowToCollection(TraktShow show)
        {
            var shows = new TraktSyncShows
            {
                Shows = new List<TraktShow>() { show }
            };

            return AddShowsToCollecton(shows);
        }

        public static TraktSyncResponse RemoveShowFromCollection(TraktShow show)
        {
            var shows = new TraktSyncShows
            {
                Shows = new List<TraktShow>() { show }
            };

            return RemoveShowsFromCollecton(shows);
        }

        public static TraktSyncResponse AddShowToCollectionEx(TraktSyncShowEx show)
        {
            var shows = new TraktSyncShowsEx
            {
                Shows = new List<TraktSyncShowEx>() { show }
            };

            return AddShowsToCollectonEx(shows);
        }

        public static TraktSyncResponse RemoveShowFromCollectionEx(TraktSyncShowEx show)
        {
            var shows = new TraktSyncShowsEx
            {
                Shows = new List<TraktSyncShowEx>() { show }
            };

            return RemoveShowsFromCollectonEx(shows);
        }

        public static TraktSyncResponse AddEpisodeToCollection(TraktSyncEpisodeCollected episode)
        {
            var episodes = new TraktSyncEpisodesCollected
            {
                Episodes = new List<TraktSyncEpisodeCollected>() { episode }
            };

            return AddEpisodesToCollecton(episodes);
        }

        public static TraktSyncResponse RemoveEpisodeFromCollection(TraktEpisode episode)
        {
            var episodes = new TraktSyncEpisodes
            {
                Episodes = new List<TraktEpisode>() { episode }
            };

            return RemoveEpisodesFromCollecton(episodes);
        }

        #endregion

        #region Watched History

        public static TraktSyncResponse AddMoviesToWatchedHistory(TraktSyncMoviesWatched movies)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryAdd, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveMoviesFromWatchedHistory(TraktSyncMovies movies)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryRemove, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToWatchedHistory(TraktSyncShows shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveShowsFromWatchedHistory(TraktSyncShows shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddEpisodesToWatchedHistory(TraktSyncEpisodesWatched episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryAdd, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveEpisodesFromWatchedHistory(TraktSyncEpisodes episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryRemove, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToWatchedHistoryEx(TraktSyncShowsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToWatchedHistoryEx(TraktSyncShowsWatchedEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveShowsFromWatchedHistoryEx(TraktSyncShowsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchedHistoryRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        #endregion

        #region Watched History (Single)

        public static TraktSyncResponse AddMovieToWatchedHistory(TraktSyncMovieWatched movie)
        {
            var movies = new TraktSyncMoviesWatched
            {
                Movies = new List<TraktSyncMovieWatched>() { movie }
            };

            return AddMoviesToWatchedHistory(movies);
        }

        public static TraktSyncResponse RemoveMovieFromWatchedHistory(TraktMovie movie)
        {
            var movies = new TraktSyncMovies
            {
                Movies = new List<TraktMovie>() { movie }
            };

            return RemoveMoviesFromWatchedHistory(movies);
        }

        public static TraktSyncResponse AddShowToWatchedHistory(TraktShow show)
        {
            var shows = new TraktSyncShows
            {
                Shows = new List<TraktShow>() { show }
            };

            return AddShowsToWatchedHistory(shows);
        }

        public static TraktSyncResponse RemoveShowFromWatchedHistory(TraktShow show)
        {
            var shows = new TraktSyncShows
            {
                Shows = new List<TraktShow>() { show }
            };

            return RemoveShowsFromWatchedHistory(shows);
        }

        public static TraktSyncResponse AddShowToWatchedHistoryEx(TraktSyncShowEx show)
        {
            var shows = new TraktSyncShowsEx
            {
                Shows = new List<TraktSyncShowEx>() { show }
            };

            return AddShowsToWatchedHistoryEx(shows);
        }

        public static TraktSyncResponse RemoveShowFromWatchedHistoryEx(TraktSyncShowEx show)
        {
            var shows = new TraktSyncShowsEx
            {
                Shows = new List<TraktSyncShowEx>() { show }
            };

            return RemoveShowsFromWatchedHistoryEx(shows);
        }

        public static TraktSyncResponse AddEpisodeToWatchedHistory(TraktSyncEpisodeWatched episode)
        {
            var episodes = new TraktSyncEpisodesWatched
            {
                Episodes = new List<TraktSyncEpisodeWatched>() { episode }
            };

            return AddEpisodesToWatchedHistory(episodes);
        }

        public static TraktSyncResponse RemoveEpisodeFromWatchedHistory(TraktEpisode episode)
        {
            var episodes = new TraktSyncEpisodes
            {
                Episodes = new List<TraktEpisode>() { episode }
            };

            return RemoveEpisodesFromWatchedHistory(episodes);
        }

        #endregion

        #region Ratings

        public static TraktSyncResponse AddMoviesToRatings(TraktSyncMoviesRated movies)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsAdd, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveMoviesFromRatings(TraktSyncMovies movies)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsRemove, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToRatings(TraktSyncShowsRated shows)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveShowsFromRatings(TraktSyncShows shows)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddEpisodesToRatings(TraktSyncEpisodesRated episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsAdd, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToRatingsEx(TraktSyncShowsRatedEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddSeasonsToRatingsEx(TraktSyncSeasonsRatedEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveEpisodesFromRatings(TraktSyncEpisodes episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsRemove, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveShowsFromRatingsEx(TraktSyncShowsRatedEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveSeasonsFromRatingsEx(TraktSyncSeasonsRatedEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncRatingsRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        #endregion

        #region Ratings (Single)

        /// <summary>
        /// Rate a single episode on trakt.tv
        /// </summary>
        public static TraktSyncResponse AddEpisodeToRatings(TraktSyncEpisodeRated episode)
        {
            var episodes = new TraktSyncEpisodesRated
            {
                Episodes = new List<TraktSyncEpisodeRated>() { episode }
            };

            return AddEpisodesToRatings(episodes);
        }

        /// <summary>
        /// UnRate a single episode on trakt.tv
        /// </summary>
        public static TraktSyncResponse RemoveEpisodeFromRatings(TraktEpisode episode)
        {
            var episodes = new TraktSyncEpisodes
            {
                Episodes = new List<TraktEpisode>() { new TraktEpisode { Ids = episode.Ids } }
            };

            return RemoveEpisodesFromRatings(episodes);
        }

        /// <summary>
        /// Rate a single episode on trakt.tv (with show info)
        /// </summary>
        public static TraktSyncResponse AddEpisodeToRatingsEx(TraktSyncShowRatedEx item)
        {
            var episodes = new TraktSyncShowsRatedEx
            {
                Shows = new List<TraktSyncShowRatedEx>() { item }
            };

            return AddShowsToRatingsEx(episodes);
        }

        /// <summary>
        /// UnRate a single episode on trakt.tv (with show info)
        /// </summary>
        public static TraktSyncResponse RemoveEpisodeFromRatingsEx(TraktSyncShowRatedEx item)
        {
            var episodes = new TraktSyncShowsRatedEx
            {
                Shows = new List<TraktSyncShowRatedEx>() { item }
            };

            return RemoveShowsFromRatingsEx(episodes);
        }

        /// <summary>
        /// Rate a single season on trakt.tv (with show info)
        /// </summary>
        public static TraktSyncResponse AddSeasonToRatingsEx(TraktSyncSeasonRatedEx item)
        {
            var seasons = new TraktSyncSeasonsRatedEx
            {
                Shows = new List<TraktSyncSeasonRatedEx>() { item }
            };

            return AddSeasonsToRatingsEx(seasons);
        }

        /// <summary>
        /// UnRate a single season on trakt.tv (with show info)
        /// </summary>
        public static TraktSyncResponse RemoveSeasonFromRatingsEx(TraktSyncSeasonRatedEx item)
        {
            var seasons = new TraktSyncSeasonsRatedEx
            {
                Shows = new List<TraktSyncSeasonRatedEx>() { item }
            };

            return RemoveSeasonsFromRatingsEx(seasons);
        }

        /// <summary>
        /// Rate a single show on trakt.tv
        /// </summary>
        public static TraktSyncResponse AddShowToRatings(TraktSyncShowRated show)
        {
            var shows = new TraktSyncShowsRated
            {
                Shows = new List<TraktSyncShowRated>() { show }
            };

            return AddShowsToRatings(shows);
        }

        /// <summary>
        /// UnRate a single show on trakt.tv
        /// </summary>
        public static TraktSyncResponse RemoveShowFromRatings(TraktShow show)
        {
            var shows = new TraktSyncShows
            {
                Shows = new List<TraktShow>() { new TraktShow { Ids = show.Ids } }
            };

            return RemoveShowsFromRatings(shows);
        }

        /// <summary>
        /// Rate a single movie on trakt.tv
        /// </summary>
        public static TraktSyncResponse AddMovieToRatings(TraktSyncMovieRated movie)
        {
            var movies = new TraktSyncMoviesRated
            {
                Movies = new List<TraktSyncMovieRated>() { movie }
            };

            return AddMoviesToRatings(movies);
        }

        /// <summary>
        /// UnRate a single movie on trakt.tv
        /// </summary>
        public static TraktSyncResponse RemoveMovieFromRatings(TraktMovie movie)
        {
            var movies = new TraktSyncMovies
            {
                Movies = new List<TraktMovie>() { new TraktMovie { Ids = movie.Ids } }
            };

            return RemoveMoviesFromRatings(movies);
        }

        #endregion

        #region Community Ratings

        public static TraktRating GetShowRatings(string id)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.ShowRatings, id));
            return response.FromJSON<TraktRating>();
        }

        public static TraktRating GetSeasonRatings(string id, int season)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.ShowRatings, id, season));
            return response.FromJSON<TraktRating>();
        }

        public static TraktRating GetEpisodeRatings(string id, int season, int episode)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.EpisodeRatings, id, season, episode));
            return response.FromJSON<TraktRating>();
        }

        #endregion

        #region Scrobble

        public static TraktScrobbleResponse StartMovieScrobble(TraktScrobbleMovie movie)
        {
            var response = PostToTrakt(TraktURIs.ScrobbleStart, movie.ToJSON());
            return response.FromJSON<TraktScrobbleResponse>();
        }

        public static TraktScrobbleResponse StartEpisodeScrobble(TraktScrobbleEpisode episode)
        {
            var response = PostToTrakt(TraktURIs.ScrobbleStart, episode.ToJSON());
            return response.FromJSON<TraktScrobbleResponse>();
        }

        public static TraktScrobbleResponse PauseMovieScrobble(TraktScrobbleMovie movie)
        {
            var response = PostToTrakt(TraktURIs.ScrobblePause, movie.ToJSON());
            return response.FromJSON<TraktScrobbleResponse>();
        }

        public static TraktScrobbleResponse PauseEpisodeScrobble(TraktScrobbleEpisode episode)
        {
            var response = PostToTrakt(TraktURIs.ScrobblePause, episode.ToJSON());
            return response.FromJSON<TraktScrobbleResponse>();
        }

        public static TraktScrobbleResponse StopMovieScrobble(TraktScrobbleMovie movie)
        {
            var response = PostToTrakt(TraktURIs.ScrobbleStop, movie.ToJSON());
            return response.FromJSON<TraktScrobbleResponse>();
        }

        public static TraktScrobbleResponse StopEpisodeScrobble(TraktScrobbleEpisode episode)
        {
            var response = PostToTrakt(TraktURIs.ScrobbleStop, episode.ToJSON());
            return response.FromJSON<TraktScrobbleResponse>();
        }

        #endregion

        #region Watchlist

        public static TraktSyncResponse AddMoviesToWatchlist(TraktSyncMovies movies)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistAdd, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveMoviesFromWatchlist(TraktSyncMovies movies)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistRemove, movies.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToWatchlist(TraktSyncShows shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddShowsToWatchlistEx(TraktSyncShowsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveShowsFromWatchlist(TraktSyncShows shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveShowsFromWatchlistEx(TraktSyncShowsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }
        
        public static TraktSyncResponse AddSeasonsToWatchlist(TraktSyncSeasonsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistAdd, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveSeasonsFromWatchlist(TraktSyncSeasonsEx shows)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistRemove, shows.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse AddEpisodesToWatchlist(TraktSyncEpisodes episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistAdd, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        public static TraktSyncResponse RemoveEpisodesFromWatchlist(TraktSyncEpisodes episodes)
        {
            var response = PostToTrakt(TraktURIs.SyncWatchlistRemove, episodes.ToJSON());
            return response.FromJSON<TraktSyncResponse>();
        }

        #endregion

        #region Watchlist (Single)

        public static TraktSyncResponse AddMovieToWatchlist(TraktMovie movie)
        {
            var movies = new TraktSyncMovies
            {
                Movies = new List<TraktMovie>() { movie }
            };

            return AddMoviesToWatchlist(movies);
        }

        public static TraktSyncResponse RemoveMovieFromWatchlist(TraktMovie movie)
        {
            var movies = new TraktSyncMovies
            {
                Movies = new List<TraktMovie>() { movie }
            };

            return RemoveMoviesFromWatchlist(movies);
        }

        public static TraktSyncResponse AddShowToWatchlist(TraktShow show)
        {
            var shows = new TraktSyncShows
            {
                Shows = new List<TraktShow>() { show }
            };

            return AddShowsToWatchlist(shows);
        }

        public static TraktSyncResponse AddShowToWatchlistEx(TraktSyncShowEx show)
        {
            var shows = new TraktSyncShowsEx
            {
                Shows = new List<TraktSyncShowEx>() { show }
            };

            return AddShowsToWatchlistEx(shows);
        }

        public static TraktSyncResponse AddSeasonToWatchlist(TraktSyncSeasonEx show)
        {
            var shows = new TraktSyncSeasonsEx
            {
                Shows = new List<TraktSyncSeasonEx>() { show }
            };

            return AddSeasonsToWatchlist(shows);
        }

        public static TraktSyncResponse RemoveSeasonFromWatchlist(TraktSyncSeasonEx show)
        {
            var shows = new TraktSyncSeasonsEx
            {
                Shows = new List<TraktSyncSeasonEx>() { show }
            };

            return RemoveSeasonsFromWatchlist(shows);
        }

        public static TraktSyncResponse RemoveShowFromWatchlist(TraktShow show)
        {
            var shows = new TraktSyncShows
            {
                Shows = new List<TraktShow>() { show }
            };

            return RemoveShowsFromWatchlist(shows);
        }

        public static TraktSyncResponse RemoveShowFromWatchlistEx(TraktSyncShowEx show)
        {
            var shows = new TraktSyncShowsEx
            {
                Shows = new List<TraktSyncShowEx>() { show }
            };

            return RemoveShowsFromWatchlistEx(shows);
        }

        public static TraktSyncResponse AddEpisodeToWatchlist(TraktEpisode episode)
        {
            var episodes = new TraktSyncEpisodes
            {
                Episodes = new List<TraktEpisode>() { episode }
            };

            return AddEpisodesToWatchlist(episodes);
        }

        public static TraktSyncResponse RemoveEpisodeFromWatchlist(TraktEpisode episode)
        {
            var episodes = new TraktSyncEpisodes
            {
                Episodes = new List<TraktEpisode>() { episode }
            };

            return RemoveEpisodesFromWatchlist(episodes);
        }

        #endregion

        #region Comments

        public static bool LikeComment(int id)
        {
            var response = PostToTrakt(string.Format(TraktURIs.CommentLike, id), null);
            return response != null;
        }

        public static bool UnLikeComment(int id)
        {
            return DeleteFromTrakt(string.Format(TraktURIs.CommentLike, id));
        }

        public static IEnumerable<TraktComment> GetCommentReplies(string id)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.CommentReplies, id));
            return response.FromJSONArray<TraktComment>();
        }

        #endregion

        #region People

        public static TraktPersonSummary GetPersonSummary(string person)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.PersonSummary, person));
            return response.FromJSON<TraktPersonSummary>();
        }

        public static TraktPersonMovieCredits GetMovieCreditsForPerson(string person)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.PersonMovieCredits, person));
            return response.FromJSON<TraktPersonMovieCredits>();
        }

        public static TraktPersonShowCredits GetShowCreditsForPerson(string person)
        {
            var response = GetFromTrakt(string.Format(TraktURIs.PersonShowCredits, person));
            return response.FromJSON<TraktPersonShowCredits>();
        }

        #endregion

        #region Web Helpers

        static string ReplaceOnTrakt(string address, string postData)
        {
            return PostToTrakt(address, postData, true, "PUT");            
        }

        static bool DeleteFromTrakt(string address)
        {
            var response = GetFromTrakt(address, "DELETE");
            return response != null;
        }

        static string GetFromTrakt(string address, string method = "GET")
        {
            WebHeaderCollection headerCollection;
            return GetFromTrakt(address, out headerCollection, method);
        }

        /// <summary>
        /// Requests data from trakt.tv 
        /// </summary>
        /// <param name="address">Address of the trakt resource</param>
        /// <param name="headerCollection">returns the headers from the response</param>
        /// <param name="method">overrides the request method: GET, DELETE, PUT</param>
        /// <param name="sendOAuth">send user access token for methods that require oAuth</param>
        /// <param name="serialiseError">return error code and description as JSON on the response when there is an error, otherwise return null (default)</param>
        static string GetFromTrakt(string address, out WebHeaderCollection headerCollection, string method = "GET", bool serialiseError = false)
        {
            headerCollection = new WebHeaderCollection();
      
            OnDataSend?.Invoke(address, null);

            Stopwatch watch;

            var request = WebRequest.Create(address) as HttpWebRequest;

            request.KeepAlive = true;
            request.Method = method;
            request.ContentLength = 0;
            request.Timeout = 120000;
            request.ContentType = "application/json";
            request.UserAgent = UserAgent;

            // add required headers for authorisation
            request.Headers.Add("trakt-api-version", "2");
            request.Headers.Add("trakt-api-key", ClientId);

            if (!string.IsNullOrEmpty(UserAccessToken))
            {
                request.Headers.Add("Authorization", string.Format("Bearer {0}", UserAccessToken));
            }

            // measure how long it took to get a response
            watch = Stopwatch.StartNew();
            string strResponse = null;

            try
            {
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                if (response == null)
                {
                    watch.Stop();
                    return null;
                }

                Stream stream = response.GetResponseStream();
                watch.Stop();

                StreamReader reader = new StreamReader(stream);
                strResponse = reader.ReadToEnd();

                headerCollection = response.Headers;

                if (method == "DELETE")
                {
                    strResponse = response.StatusCode == HttpStatusCode.NoContent ? "Item Deleted" : "Failed to delete item";
                }

                OnDataReceived?.Invoke(strResponse, response);

                OnLatency?.Invoke(watch.Elapsed.TotalMilliseconds, response, 0, strResponse.Length * sizeof(Char));

                stream.Close();
                reader.Close();
                response.Close();
            }
            catch (WebException wex)
            {
                watch.Stop();
                
                string errorMessage = wex.Message;
                if (wex.Status == WebExceptionStatus.ProtocolError)
                {
                    var response = wex.Response as HttpWebResponse;

                    string headers = string.Empty;
                    foreach (string key in response.Headers.AllKeys)
                    {
                        headers += string.Format("{0}: {1}, ", key, response.Headers[key]);
                    }
                    errorMessage = string.Format("Protocol Error, Code = '{0}', Description = '{1}', Url = '{2}', Headers = '{3}'", (int)response.StatusCode, response.StatusDescription, address, headers.TrimEnd(new char[] { ',', ' ' }));

                    strResponse = new TraktStatus { Code = (int)response.StatusCode, Description = response.StatusDescription }.ToJSON();

                    OnLatency?.Invoke(watch.Elapsed.TotalMilliseconds, response, 0, 0);

                    if (!serialiseError) return null;
                }

                OnDataError?.Invoke(errorMessage);
            }
            catch (IOException ioe)
            {
                string errorMessage = string.Format("Request failed due to an IO error, Description = '{0}', Url = '{1}', Method = '{2}'", ioe.Message, address, method);

                OnDataError?.Invoke(ioe.Message);

                strResponse = null;
            }

            return strResponse;
        }

        static string PostToTrakt(string address, string postData, bool logRequest = true, string method = "POST", string contentType = "application/json")
        {
            if (OnDataSend != null && logRequest)
                OnDataSend(address, postData);

            Stopwatch watch;

            if (postData == null)
                postData = string.Empty;

            byte[] data = new UTF8Encoding().GetBytes(postData);

            var request = WebRequest.Create(address) as HttpWebRequest;
            request.KeepAlive = true;

            request.Method = method;
            request.ContentLength = data.Length;
            request.Timeout = 120000;
            request.ContentType = contentType;
            request.UserAgent = UserAgent;

            // add required headers for authorisation
            request.Headers.Add("trakt-api-version", "2");
            request.Headers.Add("trakt-api-key", ClientId);
           
            if (!string.IsNullOrEmpty(UserAccessToken))
            {
                request.Headers.Add("Authorization", string.Format("Bearer {0}", UserAccessToken));
            }

            // measure how long it took to get a response
            watch = Stopwatch.StartNew();

            try
            {
                // post to trakt
                Stream postStream = request.GetRequestStream();
                postStream.Write(data, 0, data.Length);

                // get the response
                var response = (HttpWebResponse)request.GetResponse();
                watch.Stop();

                if (response == null)
                    return null;

                Stream responseStream = response.GetResponseStream();
                var reader = new StreamReader(responseStream);
                string strResponse = reader.ReadToEnd();

                if (string.IsNullOrEmpty(strResponse))
                {
                    strResponse = response.StatusCode.ToString();
                }

                OnDataReceived?.Invoke(strResponse, response);

                OnLatency?.Invoke(watch.Elapsed.TotalMilliseconds, response, postData.Length * sizeof(Char), strResponse.Length * sizeof(Char));

                // cleanup
                postStream.Close();
                responseStream.Close();
                reader.Close();
                response.Close();

                return strResponse;
            }
            catch (WebException ex)
            {
                watch.Stop();

                string result = null;
                string errorMessage = ex.Message;
                if (ex.Status == WebExceptionStatus.ProtocolError)
                {
                    var response = ex.Response as HttpWebResponse;

                    string headers = string.Empty;
                    foreach (string key in response.Headers.AllKeys)
                    {
                        headers += string.Format("{0}: {1}, ", key, response.Headers[key]);
                    }
                    errorMessage = string.Format("Protocol Error, Code = '{0}', Description = '{1}', Url = '{2}', Headers = '{3}'", (int)response.StatusCode, response.StatusDescription, address, headers.TrimEnd(new char[] { ',', ' ' }));

                    result = new TraktStatus { Code = (int)response.StatusCode, Description = response.StatusDescription }.ToJSON();

                    OnLatency?.Invoke(watch.Elapsed.TotalMilliseconds, response, postData.Length * sizeof(Char), 0);
                }

                // don't log an error on the authentication process if polling (status code 400)
                if (!address.Contains("oauth/device/token") || !result.Contains("400"))
                {
                    OnDataError?.Invoke(errorMessage);
                }

                return result;
            }
            catch (IOException ioe)
            {
                string errorMessage = string.Format("Request failed due to an IO error, Description = '{0}', Url = '{1}', Method = '{2}'", ioe.Message, address, method);

                OnDataError?.Invoke(ioe.Message);

                return null;
            }
        }

        #endregion

        #endregion
    }
}
