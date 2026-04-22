namespace NGB.Api.Branding;

public static class NgbStandaloneTheme
{
    public const string ThemeModeKey = "ngb.theme";

    public const string HeadScriptTag = """
                                        <script id="ngb-standalone-theme">(function(){var key='ngb.theme';function readCookie(name){var prefix=name+'=';var parts=document.cookie?document.cookie.split(';'):[];for(var i=0;i<parts.length;i++){var part=parts[i].trim();if(part.indexOf(prefix)===0)return decodeURIComponent(part.substring(prefix.length));}return null;}function readStorage(){try{return window.localStorage?window.localStorage.getItem(key):null;}catch(_){return null;}}function readMode(){var value=readCookie(key)||readStorage();return value==='light'||value==='dark'||value==='system'?value:'system';}function prefersDark(){return !!(window.matchMedia&&window.matchMedia('(prefers-color-scheme: dark)').matches);}function resolveMode(mode){return mode==='system'?(prefersDark()?'dark':'light'):mode;}function apply(){var resolved=resolveMode(readMode());var root=document.documentElement;root.classList.toggle('dark',resolved==='dark');root.setAttribute('data-ngb-theme',resolved);}apply();window.addEventListener('focus',apply);document.addEventListener('visibilitychange',function(){if(document.visibilityState==='visible')apply();});if(window.matchMedia){var media=window.matchMedia('(prefers-color-scheme: dark)');var onChange=function(){if(readMode()==='system')apply();};if(media.addEventListener)media.addEventListener('change',onChange);else if(media.addListener)media.addListener(onChange);}})();</script>
                                        """;
}
