<#import "footer.ftl" as loginFooter>

<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false>
<!DOCTYPE html>
<html class="${properties.kcHtmlClass!}" lang="${lang}"<#if realm.internationalizationEnabled> dir="${(locale.rtl)?then('rtl','ltr')}"</#if>>
<head>
  <meta charset="utf-8">
  <meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
  <#if properties.meta?has_content>
    <#list properties.meta?split(' ') as meta>
      <meta name="${meta?split('==')[0]}" content="${meta?split('==')[1]}"/>
    </#list>
  </#if>
  <title>${msg("loginTitle",(realm.displayName!''))}</title>
  <link rel="icon" href="${url.resourcesPath}/img/favicon.svg" />
  <script>
    (function () {
      var key = 'ngb.theme';
      var defaultShowPasswordLabel = '${msg("showPassword")?js_string}';
      var defaultHidePasswordLabel = '${msg("hidePassword")?js_string}';
      var eyeIconSvg = '<svg class="ngb-password-toggle-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z"></path><circle cx="12" cy="12" r="3"></circle></svg>';
      var eyeOffIconSvg = '<svg class="ngb-password-toggle-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z"></path><circle cx="12" cy="12" r="3"></circle><path d="M4 4l16 16"></path></svg>';
      function readCookie(name) {
        var prefix = name + '=';
        var parts = document.cookie ? document.cookie.split(';') : [];
        for (var i = 0; i < parts.length; i++) {
          var part = parts[i].trim();
          if (part.indexOf(prefix) === 0) return decodeURIComponent(part.substring(prefix.length));
        }
        return null;
      }
      function readStorage() {
        try {
          return window.localStorage ? window.localStorage.getItem(key) : null;
        } catch (_) {
          return null;
        }
      }
      function readMode() {
        var value = readCookie(key) || readStorage();
        return value === 'light' || value === 'dark' || value === 'system' ? value : 'system';
      }
      function prefersDark() {
        return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
      }
      function resolveMode(mode) {
        return mode === 'system' ? (prefersDark() ? 'dark' : 'light') : mode;
      }
      var root = document.documentElement;
      function apply() {
        var resolved = resolveMode(readMode());
        root.classList.toggle('dark', resolved === 'dark');
        root.setAttribute('data-ngb-theme', resolved);
      }
      function isPasswordField(input) {
        if (!input) return false;
        var type = String(input.getAttribute('type') || '').toLowerCase();
        var name = String(input.getAttribute('name') || '').toLowerCase();
        var autocomplete = String(input.getAttribute('autocomplete') || '').toLowerCase();
        return type === 'password' || name === 'password' || autocomplete.indexOf('password') >= 0;
      }
      function findPasswordInput(scope) {
        if (!scope || !scope.querySelector) return null;
        var input = scope.querySelector('input[type="password"], input[name="password"], input[autocomplete*="password"]');
        if (input) return input;
        var textInputs = scope.querySelectorAll('input[type="text"]');
        for (var i = 0; i < textInputs.length; i++) {
          if (isPasswordField(textInputs[i])) return textInputs[i];
        }
        return null;
      }
      function resolvePasswordInput(button) {
        var selector = button.getAttribute('data-password-toggle') || button.getAttribute('aria-controls') || '';
        if (selector) {
          if (selector.charAt(0) === '#') {
            return document.getElementById(selector.slice(1));
          }
          if (/^[A-Za-z][\w:-]*$/.test(selector)) {
            return document.getElementById(selector);
          }
          try {
            return document.querySelector(selector);
          } catch (_) {
          }
        }
        var group = button.closest('.pf-v5-c-input-group');
        if (!group) return null;
        return findPasswordInput(group);
      }
      function renderManagedPasswordIcon(button, isVisible) {
        button.innerHTML = isVisible ? eyeOffIconSvg : eyeIconSvg;
        button.setAttribute('data-ngb-password-icon', 'managed');
      }
      function updatePasswordToggleIcon(button, isVisible) {
        var icon = button.querySelector('[aria-hidden="true"]') || button.querySelector('i, svg, span');
        var showClass = String(button.getAttribute('data-icon-show') || '').trim();
        var hideClass = String(button.getAttribute('data-icon-hide') || '').trim();
        if (icon && icon.tagName === 'I' && showClass && hideClass) {
          icon.className = isVisible ? hideClass : showClass;
          icon.setAttribute('aria-hidden', 'true');
          return;
        }
        renderManagedPasswordIcon(button, isVisible);
      }
      function updatePasswordToggle(button, input) {
        var isVisible = input.type === 'text';
        var showLabel = button.getAttribute('data-label-show') || button.getAttribute('aria-label') || defaultShowPasswordLabel;
        var hideLabel = button.getAttribute('data-label-hide') || defaultHidePasswordLabel;
        if (!button.getAttribute('data-label-show')) button.setAttribute('data-label-show', showLabel);
        if (!button.getAttribute('data-label-hide')) button.setAttribute('data-label-hide', hideLabel);
        button.setAttribute('aria-label', isVisible ? hideLabel : showLabel);
        button.setAttribute('aria-pressed', isVisible ? 'true' : 'false');
        button.setAttribute('data-password-visible', isVisible ? 'true' : 'false');
        updatePasswordToggleIcon(button, isVisible);
      }
      function isPasswordToggleButton(button) {
        if (!button || button.tagName !== 'BUTTON') return false;
        var buttonType = String(button.type || button.getAttribute('type') || '').toLowerCase();
        if (buttonType === 'submit') return false;
        if (button.hasAttribute('data-password-toggle')) return true;
        if (button.hasAttribute('aria-controls')) return !!resolvePasswordInput(button);
        if (button.closest('.pf-v5-c-input-group')) return !!resolvePasswordInput(button);
        if (isPasswordField(resolvePasswordInput(button))) return true;
        var ariaLabel = String(button.getAttribute('aria-label') || '');
        var className = String(button.className || '');
        return /password/i.test(ariaLabel) || /password/i.test(className);
      }
      function syncPasswordToggles() {
        var buttons = document.querySelectorAll('.pf-v5-c-input-group button, button[data-password-toggle]');
        for (var i = 0; i < buttons.length; i++) {
          var button = buttons[i];
          if (!isPasswordToggleButton(button)) continue;
          var input = resolvePasswordInput(button);
          if (!input) continue;
          updatePasswordToggle(button, input);
        }
      }
      apply();
      document.addEventListener('click', function (event) {
        var target = event.target;
        var passwordToggle = target && target.closest ? target.closest('button') : null;
        if (!isPasswordToggleButton(passwordToggle)) return;

        var input = resolvePasswordInput(passwordToggle);
        if (!input) return;

        event.preventDefault();
        input.type = input.type === 'password' ? 'text' : 'password';
        updatePasswordToggle(passwordToggle, input);
      });
      if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
          apply();
          syncPasswordToggles();
        });
      } else {
        apply();
        syncPasswordToggles();
      }
      if (window.matchMedia) {
        var media = window.matchMedia('(prefers-color-scheme: dark)');
        var onChange = function () {
          if (readMode() === 'system') apply();
        };
        if (media.addEventListener) media.addEventListener('change', onChange);
        else if (media.addListener) media.addListener(onChange);
      }
    })();
  </script>
  <#if properties.stylesCommon?has_content>
    <#list properties.stylesCommon?split(' ') as style>
      <link href="${url.resourcesCommonPath}/${style}" rel="stylesheet" />
    </#list>
  </#if>
  <#if properties.styles?has_content>
    <#list properties.styles?split(' ') as style>
      <link href="${url.resourcesPath}/${style}" rel="stylesheet" />
    </#list>
  </#if>
  <#if properties.scripts?has_content>
    <#list properties.scripts?split(' ') as script>
      <script src="${url.resourcesPath}/${script}" type="text/javascript"></script>
    </#list>
  </#if>
  <script type="importmap">
  {
    "imports": {
      "rfc4648": "${url.resourcesCommonPath}/vendor/rfc4648/rfc4648.js"
    }
  }
  </script>
  <script src="${url.resourcesPath}/js/menu-button-links.js" type="module"></script>
  <#if scripts??>
    <#list scripts as script>
      <script src="${script}" type="text/javascript"></script>
    </#list>
  </#if>
  <script type="module">
    import { startSessionPolling } from "${url.resourcesPath}/js/authChecker.js";
    startSessionPolling("${url.ssoLoginInOtherTabsUrl?no_esc}");
  </script>
  <#if authenticationSession??>
  <script type="module">
    import { checkAuthSession } from "${url.resourcesPath}/js/authChecker.js";
    checkAuthSession("${authenticationSession.authSessionIdHash}");
  </script>
  </#if>
</head>
<body class="${properties.kcBodyClass!} ngb-auth-body" data-page-id="login-${pageId}">
  <div class="ngb-auth-shell">
    <#if realm.internationalizationEnabled && locale.supported?size gt 1>
      <div class="ngb-auth-locale" id="kc-locale">
        <div id="kc-locale-wrapper">
          <div id="kc-locale-dropdown" class="menu-button-links">
            <button tabindex="1" id="kc-current-locale-link" aria-label="${msg('languages')}" aria-haspopup="true" aria-expanded="false" aria-controls="language-switch1">${locale.current}</button>
            <ul role="menu" tabindex="-1" aria-labelledby="kc-current-locale-link" id="language-switch1">
              <#assign i = 1>
              <#list locale.supported as l>
                <li role="none"><a role="menuitem" id="language-${i}" href="${l.url}">${l.label}</a></li>
                <#assign i++>
              </#list>
            </ul>
          </div>
        </div>
      </div>
    </#if>

    <div class="ngb-auth-card">
      <div class="ngb-auth-card__header">
        <svg class="ngb-auth-card__logo" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 448 120" role="img" aria-label="NGB" focusable="false">
          <g fill="currentColor" transform="translate(-7 -46)">
            <path d="M523 0H333V1161Q333 1184 346 1200Q356 1213 373 1220Q395 1229 414 1225Q430 1221 440 1208L1148 210Q1167 183 1200 176Q1232 169 1266 183Q1296 195 1314 222Q1332 248 1332 281V1450H1522V281Q1522 186 1467 109Q1417 39 1337 6Q1250 -29 1161 -10Q1055 12 993 100L523 762ZM291 0V1161Q291 1194 308 1220Q326 1247 357 1259Q391 1273 422 1266Q455 1259 474 1232L1182 234Q1192 221 1209 217Q1228 213 1250 222Q1267 229 1277 243Q1289 260 1289 281V1450H1099V680L629 1343Q567 1430 461 1452Q372 1471 285 1436Q201 1401 150 1325Q100 1251 100 1161V0Z" transform="translate(0.000,160.625) scale(0.078125,-0.078125)"/>
            <path d="M1663 1450V1260H776Q554 1260 397 1103Q241 946 241 725Q241 503 397 347Q554 190 776 190H1088Q1426 190 1541 340Q1623 447 1623 725V746H804V936H1814V725Q1814 384 1692 224Q1520 0 1088 0H776Q475 0 263 212Q50 424 50 725Q50 1025 263 1237Q475 1450 776 1450ZM1663 1217H776Q572 1217 427 1073Q283 929 283 725Q283 521 427 377Q572 232 776 232H1088Q1406 232 1507 365Q1579 459 1581 704H804V513H1373Q1366 493 1356 481Q1312 423 1088 423H776Q651 423 562 511Q473 600 473 725Q473 850 562 938Q651 1027 776 1027H1663Z" transform="translate(126.719,160.625) scale(0.078125,-0.078125)"/>
            <path d="M566 0V190H1251Q1366 190 1447 271Q1529 353 1529 468Q1529 583 1447 664Q1397 715 1330 734Q1368 754 1399 785Q1481 866 1481 982Q1481 1097 1399 1178Q1318 1260 1202 1260H291V0H100V1450H1202Q1372 1450 1490 1371Q1597 1299 1641 1179Q1683 1065 1654 947Q1624 824 1529 743Q1645 670 1690 542Q1734 421 1698 296Q1661 167 1551 88Q1430 0 1251 0ZM566 232H1251Q1348 232 1417 301Q1487 370 1487 468Q1487 566 1417 635Q1348 704 1251 704H563V513H1251Q1269 513 1283 500Q1296 487 1296 468Q1296 449 1283 436Q1269 423 1251 423H566ZM563 746H1202Q1300 746 1369 815Q1438 884 1438 982Q1438 1079 1369 1148Q1300 1217 1202 1217H333V0H523V1027H1202Q1221 1027 1234 1014Q1248 1000 1248 982Q1248 963 1234 950Q1221 936 1202 936H563Z" transform="translate(273.906,160.625) scale(0.078125,-0.078125)"/>
          </g>
          <g>
            <rect x="403" y="0" width="16" height="16" fill="currentColor" />
            <rect x="422" y="0" width="16" height="16" fill="currentColor" />
            <rect x="403" y="19" width="16" height="16" fill="currentColor" />
            <rect x="422" y="19" width="16" height="16" fill="var(--ngb-logo-accent, #0F766E)" />
          </g>
        </svg>
        <#if auth?has_content && auth.showUsername() && !auth.showResetCredentials()>
          <div class="ngb-attempted-wrapper"><#nested "show-username"></div>
        </#if>
        <h1 id="kc-page-title" class="ngb-auth-card__title"><#nested "header"></h1>
      </div>

      <#if displayMessage && message?has_content>
        <div class="ngb-auth-alert ngb-auth-alert--${message.type}">
          ${kcSanitize(message.summary)?no_esc}
        </div>
      </#if>

      <div id="kc-content" class="ngb-auth-card__content">
        <#nested "form">

        <#if auth?has_content && auth.showTryAnotherWayLink()>
          <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post">
            <div class="ngb-auth-form-actions ngb-auth-form-actions--secondary">
              <button class="pf-v5-c-button pf-m-secondary" type="submit" name="tryAnotherWay" value="on">${msg("doTryAnotherWay")}</button>
            </div>
          </form>
        </#if>

        <#if realm.password && social.providers?? && social.providers?has_content>
          <div id="kc-social-providers" class="ngb-auth-social">
            <#nested "socialProviders">
          </div>
        </#if>

        <#if displayInfo>
          <div id="kc-info" class="ngb-auth-info">
            <#nested "info">
          </div>
        </#if>
      </div>

      <div class="ngb-auth-card__footer">
        <@loginFooter.content />
      </div>
    </div>
  </div>
</body>
</html>
</#macro>
