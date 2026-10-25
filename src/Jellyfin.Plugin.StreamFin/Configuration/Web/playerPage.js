// Playback uses the bundled hls.js 1.5.17 and mpegts.js 1.7.3 builds (Apache-2.0).
const PAGE_SIZE = 80;

export default function (view) {
    const state = {
        providers: [],
        providerId: '',
        kind: '',
        contentType: 'live',
        categoryId: '',
        skip: 0,
        total: 0,
        query: '',
        generation: 0,
        showId: ''
    };

    let hlsPlayer = null;
    let tsPlayer = null;
    let searchTimer = 0;
    let playGeneration = 0;
    const scripts = {};

    const providerSelect = view.querySelector('#sfwProvider');
    const tabs = view.querySelectorAll('.sfwTab');
    const search = view.querySelector('#sfwSearch');
    const cats = view.querySelector('#sfwCats');
    const grid = view.querySelector('#sfwGrid');
    const episodes = view.querySelector('#sfwEpisodes');
    const more = view.querySelector('#sfwMore');
    const status = view.querySelector('#sfwStatus');
    const empty = view.querySelector('#sfwEmpty');
    const body = view.querySelector('#sfwBody');
    const player = view.querySelector('#sfwPlayer');
    const video = view.querySelector('#sfwVideo');
    const now = view.querySelector('#sfwNow');
    const error = view.querySelector('#sfwError');

    function pick(obj, name) {
        if (!obj || typeof obj !== 'object') {
            return undefined;
        }

        if (Object.prototype.hasOwnProperty.call(obj, name)) {
            return obj[name];
        }

        const camel = name.charAt(0).toLowerCase() + name.slice(1);
        if (Object.prototype.hasOwnProperty.call(obj, camel)) {
            return obj[camel];
        }

        return undefined;
    }

    function list(obj, name) {
        const value = pick(obj, name);
        return Array.isArray(value) ? value : [];
    }

    function serverBase() {
        if (!window.ApiClient || ApiClient.serverAddress == null) {
            return '';
        }

        const address = typeof ApiClient.serverAddress === 'function'
            ? ApiClient.serverAddress.call(ApiClient)
            : ApiClient.serverAddress;
        return (address || '').replace(/\/$/, '');
    }

    function mediaUrl(path) {
        if (typeof path !== 'string' || path.indexOf('/StreamFin/Play/') !== 0) {
            return '';
        }

        return serverBase() + path;
    }

    function posterUrl(url) {
        return typeof url === 'string' && /^https?:\/\//i.test(url) ? url : '';
    }

    function setStatus(text) {
        status.textContent = text || '';
    }

    function showEmpty(text) {
        empty.hidden = !text;
        empty.textContent = text || '';
        body.hidden = !!text;
    }

    function currentProvider() {
        return state.providers.find((provider) => String(pick(provider, 'Id')) === state.providerId);
    }

    function contentAllowed(contentType) {
        const provider = currentProvider();
        if (!provider) {
            return false;
        }

        if (contentType === 'movies') {
            return pick(provider, 'Movies') !== false;
        }

        if (contentType === 'series') {
            return pick(provider, 'Series') !== false;
        }

        return pick(provider, 'Live') !== false;
    }

    function applyTabs() {
        tabs.forEach((tab) => {
            const content = tab.getAttribute('data-content');
            tab.hidden = !contentAllowed(content);
            tab.classList.toggle('sfwOn', content === state.contentType);
        });

        if (!contentAllowed(state.contentType)) {
            const next = ['live', 'movies', 'series'].find(contentAllowed);
            state.contentType = next || 'live';
            tabs.forEach((tab) => {
                tab.classList.toggle('sfwOn', tab.getAttribute('data-content') === state.contentType);
            });
        }
    }

    function get(route, query) {
        const params = new URLSearchParams();
        Object.keys(query || {}).forEach((key) => {
            const value = query[key];
            if (value !== undefined && value !== null && value !== '') {
                params.append(key, value);
            }
        });

        const url = ApiClient.getUrl(route);
        const qs = params.toString();
        return ApiClient.getJSON(qs ? url + (url.indexOf('?') >= 0 ? '&' : '?') + qs : url);
    }

    function loadScript(name) {
        if (scripts[name]) {
            return scripts[name];
        }

        scripts[name] = new Promise((resolve, reject) => {
            const existing = document.querySelector('script[data-streamfin="' + name + '"]');
            if (existing) {
                resolve();
                return;
            }

            const script = document.createElement('script');
            script.src = ApiClient.getUrl('web/ConfigurationPage', { name: name });
            script.async = true;
            script.dataset.streamfin = name;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error('Could not load the player.'));
            document.head.appendChild(script);
        });

        return scripts[name];
    }

    function stopPlayback() {
        if (hlsPlayer) {
            hlsPlayer.destroy();
            hlsPlayer = null;
        }

        if (tsPlayer) {
            try {
                tsPlayer.pause();
                tsPlayer.unload();
                tsPlayer.detachMediaElement();
                tsPlayer.destroy();
            } catch (ignore) {
                // The previous channel is already gone.
            }

            tsPlayer = null;
        }

        video.pause();
        video.removeAttribute('src');
        video.load();
    }

    function playNative(url) {
        return new Promise((resolve, reject) => {
            const onError = () => {
                video.removeEventListener('error', onError);
                reject(new Error('native'));
            };

            video.addEventListener('error', onError);
            video.src = url;
            const started = video.play();
            if (started && started.then) {
                started.then(resolve, reject);
            } else {
                resolve();
            }
        });
    }

    function playHls(url) {
        return loadScript('streamfin-hls.js').then(() => new Promise((resolve, reject) => {
            if (!window.Hls || !Hls.isSupported()) {
                playNative(url).then(resolve, reject);
                return;
            }

            const hls = new Hls({ enableWorker: false, lowLatencyMode: false });
            hlsPlayer = hls;
            hls.on(Hls.Events.ERROR, function (_, data) {
                if (!data || !data.fatal) {
                    return;
                }

                hls.destroy();
                if (hlsPlayer === hls) {
                    hlsPlayer = null;
                }

                reject(new Error(data.details || 'hls'));
            });
            hls.on(Hls.Events.MANIFEST_PARSED, function () {
                const started = video.play();
                if (started && started.catch) {
                    started.catch(function () { /* the user can press play */ });
                }

                resolve();
            });
            hls.loadSource(url);
            hls.attachMedia(video);
        }));
    }

    function playTs(url) {
        return loadScript('streamfin-mpegts.js').then(() => new Promise((resolve, reject) => {
            if (!window.mpegts || !mpegts.isSupported || !mpegts.isSupported()) {
                reject(new Error('ts'));
                return;
            }

            const playerTs = mpegts.createPlayer({
                type: 'mse',
                isLive: true,
                url: url
            }, {
                enableWorker: false,
                lazyLoad: false
            });
            tsPlayer = playerTs;
            playerTs.on(mpegts.Events.ERROR, function () {
                reject(new Error('ts'));
            });
            playerTs.attachMediaElement(video);
            playerTs.load();
            const started = playerTs.play();
            if (started && started.then) {
                started.then(resolve, resolve);
            } else {
                resolve();
            }
        }));
    }

    function play(item, label) {
        const primary = mediaUrl(pick(item, 'PlayUrl'));
        const fallback = mediaUrl(pick(item, 'FallbackPlayUrl'));
        const container = String(pick(item, 'Container') || '');
        const live = pick(item, 'Live') === true;
        const generation = ++playGeneration;

        stopPlayback();
        player.hidden = false;
        now.textContent = label || pick(item, 'Name') || pick(item, 'Title') || '';
        error.textContent = '';

        const failed = () => {
            if (generation !== playGeneration) {
                return;
            }

            error.textContent = live
                ? 'This channel did not start. The provider may be offline, or the browser cannot play this format.'
                : 'This title did not start in the browser. The panel may be serving a format the browser cannot play.';
        };

        if (!primary) {
            failed();
            return;
        }

        const start = (live || container === 'm3u8')
            ? playHls(primary).catch(() => (fallback ? playTs(fallback) : Promise.reject(new Error('hls'))))
            : playNative(primary);

        start.catch(failed);
    }

    function markSelected(id) {
        view.querySelectorAll('.sfwOn').forEach((node) => {
            if (node.classList.contains('sfwCard') || node.classList.contains('sfwEpisode')) {
                node.classList.toggle('sfwOn', node.getAttribute('data-id') === id);
            }
        });
    }

    function renderCategories(categories) {
        cats.textContent = '';
        categories.forEach((category) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'sfwCat';
            button.textContent = pick(category, 'Name') || pick(category, 'Id') || 'Category';
            button.setAttribute('data-id', pick(category, 'Id') || '');
            if (button.getAttribute('data-id') === state.categoryId) {
                button.classList.add('sfwOn');
            }

            button.addEventListener('click', () => {
                state.categoryId = button.getAttribute('data-id');
                state.skip = 0;
                state.showId = '';
                cats.querySelectorAll('.sfwCat').forEach((node) => {
                    node.classList.toggle('sfwOn', node === button);
                });
                episodes.hidden = true;
                grid.hidden = false;
                loadItems(false);
            });
            cats.appendChild(button);
        });
    }

    function appendItems(items) {
        items.forEach((item) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'sfwCard';
            button.setAttribute('data-id', String(pick(item, 'Id') || ''));

            const image = document.createElement('img');
            image.className = 'sfwPoster' + (pick(item, 'Live') ? ' sfwLogo' : '');
            image.alt = '';
            const poster = posterUrl(pick(item, 'Poster'));
            if (poster) {
                image.src = poster;
            } else {
                image.hidden = true;
            }

            const name = document.createElement('div');
            name.className = 'sfwName';
            name.textContent = pick(item, 'Name') || 'Untitled';

            button.appendChild(image);
            button.appendChild(name);

            const metaBits = [];
            if (pick(item, 'ChannelNumber')) {
                metaBits.push('Ch ' + pick(item, 'ChannelNumber'));
            }

            const rating = Number(pick(item, 'Rating'));
            if (rating > 0) {
                metaBits.push(rating.toFixed(1));
            }

            if (pick(item, 'Series')) {
                metaBits.push('Show');
            }

            if (metaBits.length) {
                const meta = document.createElement('div');
                meta.className = 'sfwMeta';
                meta.textContent = metaBits.join(' · ');
                button.appendChild(meta);
            }

            button.addEventListener('click', () => {
                markSelected(button.getAttribute('data-id'));
                button.classList.add('sfwOn');
                if (pick(item, 'Series')) {
                    openShow(item);
                } else {
                    play(item, pick(item, 'Name'));
                }
            });
            grid.appendChild(button);
        });
    }

    function renderEpisodes(show, rows) {
        episodes.textContent = '';
        grid.hidden = true;
        more.hidden = true;
        episodes.hidden = false;

        const back = document.createElement('button');
        back.type = 'button';
        back.className = 'sfwBtn';
        back.textContent = 'Back to shows';
        back.addEventListener('click', () => {
            state.showId = '';
            episodes.hidden = true;
            grid.hidden = false;
            more.hidden = state.skip + PAGE_SIZE >= state.total;
        });
        episodes.appendChild(back);

        const title = document.createElement('h3');
        title.className = 'sfwSeason';
        title.textContent = pick(show, 'Name') || 'Show';
        episodes.appendChild(title);

        const plot = pick(show, 'Plot');
        if (plot) {
            const plotNode = document.createElement('div');
            plotNode.className = 'sfwPlot';
            plotNode.textContent = plot;
            episodes.appendChild(plotNode);
        }

        let season = null;
        rows.forEach((row) => {
            const rowSeason = Number(pick(row, 'Season') || 0);
            if (rowSeason !== season) {
                season = rowSeason;
                const heading = document.createElement('div');
                heading.className = 'sfwSeason';
                heading.textContent = 'Season ' + rowSeason;
                episodes.appendChild(heading);
            }

            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'sfwEpisode';
            button.setAttribute('data-id', String(pick(row, 'Id') || ''));
            const number = Number(pick(row, 'Number') || 0);
            const label = 'S' + rowSeason + ' E' + number + '  ' + (pick(row, 'Title') || 'Episode');
            button.textContent = label;
            button.addEventListener('click', () => {
                episodes.querySelectorAll('.sfwEpisode').forEach((node) => node.classList.remove('sfwOn'));
                button.classList.add('sfwOn');
                play(row, label);
            });
            episodes.appendChild(button);
        });

        if (!rows.length) {
            const none = document.createElement('div');
            none.className = 'sfwStatus';
            none.textContent = 'This show has no episodes.';
            episodes.appendChild(none);
        }
    }

    function openShow(item) {
        const generation = ++state.generation;
        state.showId = String(pick(item, 'Id') || '');
        setStatus('Loading episodes…');
        episodes.hidden = true;
        get('StreamFin/Browse/Episodes', {
            providerId: state.providerId,
            seriesId: state.showId
        }).then((response) => {
            if (generation !== state.generation) {
                return;
            }

            if (!pick(response, 'Success')) {
                setStatus(pick(response, 'Message') || 'Episodes could not be loaded.');
                return;
            }

            setStatus('');
            renderEpisodes(item, list(response, 'Episodes'));
        }, () => {
            if (generation === state.generation) {
                setStatus('Episodes could not be loaded.');
            }
        });
    }

    function loadItems(append) {
        const generation = ++state.generation;
        if (!append) {
            grid.textContent = '';
            episodes.hidden = true;
            grid.hidden = false;
            state.skip = 0;
        }

        setStatus(append ? 'Loading more…' : 'Loading this category…');
        more.hidden = true;

        get('StreamFin/Browse/Items', {
            providerId: state.providerId,
            contentType: state.contentType,
            categoryId: state.categoryId,
            skip: state.skip,
            take: PAGE_SIZE,
            q: state.query
        }).then((response) => {
            if (generation !== state.generation) {
                return;
            }

            if (!pick(response, 'Success')) {
                setStatus(pick(response, 'Message') || 'This category could not be loaded.');
                return;
            }

            const items = list(response, 'Items');
            state.total = Number(pick(response, 'Total') || 0);
            state.skip = Number(pick(response, 'Skip') || 0) + items.length;
            appendItems(items);

            let note = state.total
                ? 'Showing ' + Math.min(state.skip, state.total) + ' of ' + state.total
                : 'Nothing in this category.';
            if (pick(response, 'Playlist')) {
                note += '. Playlist groups open from the copy already downloaded.';
            } else if (pick(response, 'FullCatalog')) {
                note += '. This provider sent the whole list once. Other categories open from that.';
            }

            setStatus(note);
            more.hidden = state.skip >= state.total || state.showId;
        }, () => {
            if (generation === state.generation) {
                setStatus('This category could not be loaded.');
            }
        });
    }

    function loadCategories() {
        const generation = ++state.generation;
        state.categoryId = '';
        state.showId = '';
        cats.textContent = '';
        grid.textContent = '';
        episodes.hidden = true;
        more.hidden = true;
        const provider = currentProvider();
        const kind = provider ? String(pick(provider, 'Kind') || '') : '';
        state.kind = kind;
        setStatus(kind === 'M3u'
            ? 'Downloading the playlist once. Groups open immediately after that.'
            : 'Loading categories…');

        get('StreamFin/Browse/Categories', {
            providerId: state.providerId,
            contentType: state.contentType
        }).then((response) => {
            if (generation !== state.generation) {
                return;
            }

            if (!pick(response, 'Success')) {
                setStatus(pick(response, 'Message') || 'Categories could not be loaded.');
                return;
            }

            const categories = list(response, 'Categories');
            if (!categories.length) {
                setStatus('This subscription has no categories here.');
                return;
            }

            state.categoryId = String(pick(categories[0], 'Id') || '');
            renderCategories(categories);
            loadItems(false);
        }, () => {
            if (generation === state.generation) {
                setStatus('Categories could not be loaded.');
            }
        });
    }

    function loadProviders() {
        showEmpty('');
        get('StreamFin/Browse/Providers').then((response) => {
            const providers = list(response, 'Providers');
            state.providers = providers;
            providerSelect.textContent = '';

            if (!providers.length) {
                showEmpty('Save a subscription on the StreamFin IPTV page, then come back here.');
                return;
            }

            providers.forEach((provider) => {
                const option = document.createElement('option');
                option.value = String(pick(provider, 'Id') || '');
                option.textContent = pick(provider, 'Name') || 'IPTV';
                providerSelect.appendChild(option);
            });

            state.providerId = providerSelect.value;
            applyTabs();
            loadCategories();
        }, () => {
            showEmpty('Subscriptions could not be loaded. Save one on the StreamFin IPTV page, then come back here.');
        });
    }

    providerSelect.addEventListener('change', () => {
        state.providerId = providerSelect.value;
        state.query = '';
        search.value = '';
        applyTabs();
        loadCategories();
    });

    tabs.forEach((tab) => {
        tab.addEventListener('click', () => {
            const content = tab.getAttribute('data-content');
            if (!content || content === state.contentType) {
                return;
            }

            state.contentType = content;
            state.query = '';
            search.value = '';
            applyTabs();
            loadCategories();
        });
    });

    search.addEventListener('input', () => {
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => {
            state.query = search.value.trim();
            state.showId = '';
            if (state.categoryId) {
                loadItems(false);
            }
        }, 250);
    });

    more.addEventListener('click', () => loadItems(true));

    view.querySelector('#sfwSettings').addEventListener('click', () => {
        Dashboard.navigate(Dashboard.getPluginUrl('StreamFin IPTV'));
    });

    view.addEventListener('viewshow', () => {
        loadScript('streamfin-hls.js').catch(function () { /* reported when playback starts */ });
        loadScript('streamfin-mpegts.js').catch(function () { /* reported when playback starts */ });
        loadProviders();
    });

    view.addEventListener('viewhide', () => {
        playGeneration += 1;
        stopPlayback();
    });
    view.addEventListener('viewdestroy', () => {
        playGeneration += 1;
        stopPlayback();
    });
}
