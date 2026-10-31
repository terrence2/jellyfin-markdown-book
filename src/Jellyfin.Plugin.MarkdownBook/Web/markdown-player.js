/**
 * Jellyfin.Plugin.MarkdownBook — client-side book player for rendered Markdown.
 *
 * Served by the plugin's API controller and injected into jellyfin-web by the
 * File Transformation plugin. Must run before the (deferred) app bundle so the
 * player can be registered when jellyfin-web loads its plugin list:
 *
 *   1. this script sets window['markdownPlayer/plugin'] (window-globals plugin path
 *      of pluginManager.loadPlugin — runs first because the tag is blocking);
 *   2. config.json gains "markdownPlayer/plugin" in its "plugins" array, which
 *      makes jellyfin-web instantiate the class with real module dependencies.
 */

/* global document, window, fetch */

(function () {
    'use strict';

    var PLUGIN_ID = 'markdownbookplayer';
    var OVERLAY_ID = 'markdownBookReaderOverlay';
    var STYLE_ID = 'markdownBookReaderStyles';

    var STYLES =
        '.markdownBookReaderOverlay {' +
        '  position: fixed; z-index: 99998; inset: 0;' +
        '  background: #101010; overflow: hidden;' +
        '}' +
        '.markdownBookReaderOverlay .markdownBookReaderClose {' +
        '  position: fixed; top: 0.6em; right: 0.9em; z-index: 3;' +
        '  padding: 0.35em 0.6em; font-size: 200%; line-height: 1;' +
        '  color: #909090; background: none; border: 0; cursor: pointer;' +
        '}' +
        '.markdownBookReaderOverlay .markdownBookReaderClose:hover { color: #fff; }' +
        '.markdownBookReaderOverlay .markdownBookReaderScroll {' +
        '  position: absolute; inset: 0; overflow-y: auto; overflow-x: hidden;' +
        '}' +
        '.markdownBookReaderOverlay .markdownBookReaderScroll::-webkit-scrollbar { width: 8px; }' +
        '.markdownBookReaderOverlay .markdownBookReaderScroll::-webkit-scrollbar-thumb { background: #404040; border-radius: 4px; }' +
        '.markdownBookReaderOverlay .markdownBookReaderArticle {' +
        '  max-width: 46em; margin: 0 auto; padding: 4.5em 4.5em 8em;' +
        '  font-family: Georgia, "Times New Roman", serif; font-size: 108%;' +
        '  line-height: 1.7; color: #d8dadc;' +
        '}' +
        '.markdownBookReaderArticle h1, .markdownBookReaderArticle h2, .markdownBookReaderArticle h3,' +
        '.markdownBookReaderArticle h4, .markdownBookReaderArticle h5, .markdownBookReaderArticle h6 {' +
        '  font-family: inherit; line-height: 1.3; margin: 1.4em 0 0.6em; color: #fff;' +
        '  scroll-margin-top: 2em;' +
        '}' +
        '.markdownBookReaderArticle h1 { font-size: 1.7em; margin-top: 0.4em; border-bottom: 1px solid #2a2a2a; padding-bottom: 0.25em; }' +
        '.markdownBookReaderArticle h2 { font-size: 1.4em; }' +
        '.markdownBookReaderArticle h3 { font-size: 1.2em; }' +
        '.markdownBookReaderArticle p { margin: 0.85em 0; }' +
        '.markdownBookReaderArticle a { color: #00a4dc; }' +
        '.markdownBookReaderArticle img { max-width: 100%; height: auto; }' +
        '.markdownBookReaderArticle pre, .markdownBookReaderArticle code {' +
        '  font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 0.88em;' +
        '  background: #171717; border-radius: 3px;' +
        '}' +
        '.markdownBookReaderArticle code { padding: 0.1em 0.35em; }' +
        '.markdownBookReaderArticle pre { padding: 1em; overflow-x: auto; border: 1px solid #2a2a2a; }' +
        '.markdownBookReaderArticle pre code { padding: 0; background: none; }' +
        '.markdownBookReaderArticle blockquote { margin: 1em 0; padding: 0.1em 1.1em; color: #a0a0a0; border-left: 3px solid #3a3a3a; }' +
        '.markdownBookReaderArticle table { border-collapse: collapse; margin: 1em 0; }' +
        '.markdownBookReaderArticle th, .markdownBookReaderArticle td { border: 1px solid #3a3a3a; padding: 0.35em 0.8em; }' +
        '.markdownBookReaderArticle hr { border: 0; border-top: 1px solid #2a2a2a; margin: 1.6em 0; }';

    function MarkdownBookPlayer(deps) {
        this.name = 'Markdown Book Player';
        this.type = 'mediaplayer';
        this.id = PLUGIN_ID;
        this.priority = 0; // beat the built-in book/pdf players (priority 1)
        this.deps = deps;
        this.item = null;
        this.overlay = null;
        this._candidateItem = null;

        // deps only exist once pluginManager instantiates us (script load is too
        // early — playbackManager isn't on window yet), so install here.
        installPlayProbe(deps);

        this._onWindowKeyDown = this._onWindowKeyDown.bind(this);
        this._onCloseClick = this._close.bind(this);
    }

    MarkdownBookPlayer.prototype.canPlayMediaType = function (mediaType) {
        return (mediaType || '').toLowerCase() === 'book';
    };

    // Claim the item before the built-in bookPlayer (item.Path?.endsWith('epub'))
    // or pdfPlayer can reject it. Container ("md") is always present on the client
    // DTO, while Path may be withheld for non-admin users.
    MarkdownBookPlayer.prototype.canPlayItem = function (item) {
        if (!item) {
            return false;
        }

        this._candidateItem = item;
        return (item.Container || '').toLowerCase() === 'md'
            || /\.md$/i.test(item.Path || '');
    };

    // jellyfin-web routes Book playback through player.play(...) with either the
    // raw play options ({items: [...]}, main Play button) or a stream-info object
    // ({item: ...}, other flows). canPlayItem() runs just before play(), so
    // _candidateItem is a reliable fallback for both shapes.
    MarkdownBookPlayer.prototype.play = function (playInfo) {
        var item = (playInfo && (playInfo.items && playInfo.items[0] || playInfo.item))
            || this._candidateItem;
        var deps = this.deps;

        if (!item || !item.ServerId || !item.Id) {
            return Promise.reject(new Error('[MarkdownBook] no playable item'));
        }

        this.item = item;
        this.currentSrcValue = 'MarkdownBook/' + item.Id + '/render';

        var api = deps.ServerConnections.getApiClient(item.ServerId);
        if (!api) {
            return Promise.reject(new Error('[MarkdownBook] no api client for server ' + item.ServerId));
        }

        var url = api.getUrl(this.currentSrcValue);

        this.started = false;
        deps.loading.show();

        var self = this;
        // Jellyfin 12 rejects the api_key query parameter by default; raw fetch must
        // supply the token header that the app's own api client adds automatically.
        return fetch(url, {
            headers: { authorization: 'MediaBrowser Token="' + api.accessToken() + '"' }
        })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('[MarkdownBook] render request failed: HTTP ' + response.status);
                }

                return response.text();
            })
            .then(function (html) {
                self._show(html);
                self.started = true;
            })
            .catch(function (err) {
                deps.loading.hide();
                if (self.deps.toast) {
                    self.deps.toast('Unable to read Markdown book.');
                }
                console.error(err);
                return Promise.reject(err);
            });
    };

    MarkdownBookPlayer.prototype._show = function (html) {
        this._disposeOverlay();

        var style = document.getElementById(STYLE_ID);
        if (!style) {
            style = document.createElement('style');
            style.id = STYLE_ID;
            style.textContent = STYLES;
            document.head.appendChild(style);
        }

        var overlay = document.createElement('div');
        overlay.id = OVERLAY_ID;
        overlay.className = 'markdownBookReaderOverlay';

        var closeButton = document.createElement('button');
        closeButton.className = 'markdownBookReaderClose';
        closeButton.setAttribute('title', 'Close');
        closeButton.setAttribute('aria-label', 'Close');
        closeButton.textContent = '×';
        closeButton.addEventListener('click', this._onCloseClick);

        var scroller = document.createElement('div');
        scroller.className = 'markdownBookReaderScroll';

        var article = document.createElement('article');
        article.className = 'markdownBookReaderArticle';
        article.id = 'markdownReaderContent';
        article.innerHTML = html; // rendered and sanitized server-side (Markdig, raw HTML disabled by default)

        scroller.appendChild(article);
        overlay.appendChild(closeButton);
        overlay.appendChild(scroller);
        document.body.appendChild(overlay);

        this.overlay = overlay;
        this.deps.loading.hide();

        window.addEventListener('keydown', this._onWindowKeyDown, false);
    };

    // Close on Escape; scroll by a "page" on Page Up/Down since arrow keys are
    // usually captured for playback OSDs while reading.
    MarkdownBookPlayer.prototype._onWindowKeyDown = function (event) {
        if (!this.overlay) {
            return;
        }

        var scroller = this.overlay.querySelector('.markdownBookReaderScroll');

        if (event.key === 'Escape') {
            event.preventDefault();
            this._close();
        } else if (event.key === 'PageDown' || event.key === 'PageUp') {
            event.preventDefault();
            if (scroller) {
                scroller.scrollTop += (event.key === 'PageDown' ? 1 : -1) * scroller.clientHeight * 0.85;
            }
        }
    };

    MarkdownBookPlayer.prototype._close = function () {
        this._disposeOverlay();

        // bookPlayer-compatible stop notification; playbackManager binds this to
        // reset playback state (bindStopped).
        this.started = false;
        this.deps.events && this.deps.events.trigger(this, 'stopped', [{ src: this.item }]);

        var item = this.item;
        this.item = null;
        return item;
    };

    MarkdownBookPlayer.prototype._disposeOverlay = function () {
        window.removeEventListener('keydown', this._onWindowKeyDown, false);

        if (this.overlay && this.overlay.parentNode) {
            this.overlay.parentNode.removeChild(this.overlay);
        }

        this.overlay = null;
    };

    // --- minimal player API expected by playbackManager -----------------------

    MarkdownBookPlayer.prototype.stop = function () {
        return Promise.resolve(this._close());
    };

    MarkdownBookPlayer.prototype.destroy = function () {
        this._disposeOverlay();
        this.item = null;
    };

    MarkdownBookPlayer.prototype.currentItem = function () {
        return this.item;
    };

    MarkdownBookPlayer.prototype.currentMediaSource = function () {
        return null;
    };

    MarkdownBookPlayer.prototype.paused = function () {
        return !this.started;
    };

    MarkdownBookPlayer.prototype.currentTime = function () {
        return 0;
    };

    MarkdownBookPlayer.prototype.duration = function () {
        return 0;
    };

    MarkdownBookPlayer.prototype.currentSrc = function () {
        return this.currentSrcValue;
    };

    window['markdownPlayer/plugin'] = function () {
        return MarkdownBookPlayer; // pluginManager instantiates `new MarkdownBookPlayer(deps)`
    };

    // --------------------------------------------------------------------------
    // Book items fetched by the web client carry neither Path (DtoService only maps
    // it when Fields=Path was requested) nor a usable Container (not persisted), so
    // neither we nor the built-in epub/pdf players can claim them from the Play
    // button alone. Patch playbackManager.play to probe item details once for
    // Path-less Book items: .md gets stamped Container="md" (we win the selection),
    // epub/pdf get their Path stamped (the built-in players win as designed).
    function installPlayProbe(deps) {
        var playbackManager = deps.playbackManager;
        if (!playbackManager || typeof playbackManager.play !== 'function'
            || playbackManager.__markdownBookProbe) {
            return;
        }

        playbackManager.__markdownBookProbe = true;

        var originalPlay = playbackManager.play;

        playbackManager.play = function () {
            var args = Array.prototype.slice.call(arguments);
            var first = args[0];

            var candidate = null;
            if (Array.isArray(first) && first.length) {
                candidate = first[0];
            } else if (first && first.items && first.items.length) {
                candidate = first.items[0];
            } else if (first && first.Type) {
                candidate = first;
            }

            if (!candidate || candidate.Type !== 'Book' || candidate.__markdownBookProbed
                || candidate.Path || candidate.Container) {
                return originalPlay.apply(playbackManager, args);
            }

            var api = deps.ServerConnections.getApiClient(candidate.ServerId);
            if (!api || !api.getCurrentUserId()) {
                return originalPlay.apply(playbackManager, args);
            }

            var url = api.getUrl('Users/' + api.getCurrentUserId() + '/Items/' + candidate.Id, {
                Fields: 'Path'
            });

            candidate.__markdownBookProbed = true;

            return fetch(url, {
                headers: { authorization: 'MediaBrowser Token="' + api.accessToken() + '"' }
            })
                .then(function (response) {
                    return response.ok ? response.json() : null;
                })
                .then(function (details) {
                    if (details && details.Path) {
                        candidate.Path = details.Path;
                        if (/\.md$/i.test(details.Path)) {
                            candidate.Container = 'md';
                        }
                    }
                })
                .catch(function () { /* proceed with unprobed item */ })
                .then(function () {
                    return originalPlay.apply(playbackManager, args);
                });
        };
    }
})();