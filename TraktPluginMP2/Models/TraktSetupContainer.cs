﻿using TraktPluginMP2.Services;
using TraktPluginMP2.Web;

namespace TraktPluginMP2.Models
{
  internal static class TraktSetupContainer
  {
    internal static TraktSetupManager ResolveManager()
    {
      IMediaPortalServices mediaPortalServices = new MediaPortalServices();
      IWebRequestExt webRequestExt = new WebRequestExt();
      ITraktWeb traktWeb = new TraktWeb(webRequestExt, mediaPortalServices.GetLogger());
      ITraktAuth traktAuth = new TraktAuth(traktWeb);
      ITraktLogin traktLogin = new TraktLogin(traktAuth, mediaPortalServices);
      ITraktAPI traktApi = new TraktAPIWrapper();
      ITraktCache traktCache = new TraktCache(mediaPortalServices, traktApi);
      ITraktServices traktServices = new TraktServices(traktCache, traktLogin, traktApi);

      return new TraktSetupManager(mediaPortalServices, traktServices);
    }
  }
}