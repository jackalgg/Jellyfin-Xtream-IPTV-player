const PLUGIN_ID = 'cdf3b2f9-ba0b-41f1-8ebc-e340588673ab';

const FILTERS = [
    { field: 'LiveFilter', contentType: 'live' },
    { field: 'MovieFilter', contentType: 'movies' },
    { field: 'SeriesFilter', contentType: 'series' }
];

const PROVIDER_DEFAULTS = {
    Name: 'My IPTV',
    Enabled: true,
    Kind: 'Xtream',
    ServerUrl: '',
    Username: '',
    Password: '',
    PlaylistUrl: '',
    EpgUrl: '',
    LiveStreamFormat: 'MpegTs',
    UserAgent: '',
    Referer: '',
    MaxConcurrentStreams: 0,
    EnableLiveTv: true,
    EnableMovies: true,
    EnableSeries: true,
    UseProviderChannelNumbers: true,
    ChannelNumberPrefix: ''
};

const GLOBAL_FIELDS = [
    { id: 'sfLibraryRoot', key: 'LibraryRoot', type: 'text' },
    { id: 'sfSyncIntervalHours', key: 'SyncIntervalHours', type: 'int' },
    { id: 'sfGuideDays', key: 'GuideDays', type: 'int' },
    { id: 'sfImportLibraries', key: 'ImportLibraries', type: 'bool' },
    { id: 'sfAutoProvision', key: 'AutoProvision', type: 'bool' },
    { id: 'sfWriteNfoMetadata', key: 'WriteNfoMetadata', type: 'bool' },
    { id: 'sfEnableInternetMetadata', key: 'EnableInternetMetadata', type: 'bool' },
    { id: 'sfPruneRemovedItems', key: 'PruneRemovedItems', type: 'bool' },
    { id: 'sfRequestTimeoutSeconds', key: 'RequestTimeoutSeconds', type: 'int' },
    { id: 'sfCacheMinutes', key: 'CacheMinutes', type: 'int' },
    { id: 'sfSeriesConcurrency', key: 'SeriesConcurrency', type: 'int' },
    { id: 'sfMovieDetailBudget', key: 'MovieDetailBudget', type: 'int' },
    { id: 'sfFetchMovieDetails', key: 'FetchMovieDetails', type: 'bool' },
    { id: 'sfProxyPlayback', key: 'ProxyPlayback', type: 'bool' },
    { id: 'sfPreferDirectPlay', key: 'PreferDirectPlay', type: 'bool' },
    { id: 'sfResolveStreamRedirects', key: 'ResolveStreamRedirects', type: 'bool' },
    { id: 'sfAllowUntrustedCertificates', key: 'AllowUntrustedCertificates', type: 'bool' },
    { id: 'sfVerboseLogging', key: 'VerboseLogging', type: 'bool' }
];

function blankProvider(index) {
    return Object.assign({ Id: newProviderId() }, PROVIDER_DEFAULTS, {
        Name: index === 1 ? 'My IPTV' : 'IPTV ' + index,
        LiveFilter: { Mode: 'All', Categories: [], TitleExcludePatterns: [], MaxItemsPerCategory: 0 },
        MovieFilter: { Mode: 'All', Categories: [], TitleExcludePatterns: [], MaxItemsPerCategory: 0 },
        SeriesFilter: { Mode: 'All', Categories: [], TitleExcludePatterns: [], MaxItemsPerCategory: 0 }
    });
}

function setField(host, key, value) {
    const input = host.querySelector('[data-sf-field="' + key + '"]');
    if (!input) {
        return;
    }

    if (input.type === 'checkbox') {
        input.checked = value !== false;
    } else {
        input.value = value == null ? '' : value;
    }
}

function applyPastedLink(provider, host, raw) {
    const text = (raw || '').trim();
    if (!text) {
        return false;
    }

    let url;
    try {
        url = new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(text) ? text : 'http://' + text);
    } catch (error) {
        return false;
    }

    const username = url.searchParams.get('username');
    const password = url.searchParams.get('password');
    if (!username && !password) {
        return false;
    }

    provider.Kind = 'Xtream';
    provider.ServerUrl = url.origin;
    provider.Username = username || provider.Username || '';
    provider.Password = password || provider.Password || '';

    setField(host, 'Kind', provider.Kind);
    setField(host, 'ServerUrl', provider.ServerUrl);
    setField(host, 'Username', provider.Username);
    setField(host, 'Password', provider.Password);
    return true;
}

function newProviderId() {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

function post(path, body, query) {
    return ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('StreamFin/' + path, query),
        data: body === undefined ? undefined : JSON.stringify(body),
        contentType: 'application/json',
        dataType: 'json'
    });
}

function postNoContent(path) {
    // dataType text makes the dashboard read an empty 204 body instead of leaving the
    // connection open, which otherwise stalls the next request with no success and no error.
    return ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('StreamFin/' + path),
        dataType: 'text'
    });
}

function plural(count, singular, pluralForm) {
    return count.toLocaleString() + ' ' + (count === 1 ? singular : (pluralForm || singular + 's'));
}

function formatWhen(iso) {
    if (!iso) {
        return 'never';
    }

    const when = new Date(iso);
    if (Number.isNaN(when.getTime())) {
        return 'never';
    }

    const minutes = Math.round((Date.now() - when.getTime()) / 60000);
    if (minutes < 1) {
        return 'just now';
    }

    if (minutes < 60) {
        return plural(minutes, 'minute') + ' ago';
    }

    const hours = Math.round(minutes / 60);
    if (hours < 36) {
        return plural(hours, 'hour') + ' ago';
    }

    return when.toLocaleDateString();
}

function setResult(container, ok, html) {
    container.innerHTML = '<div class="sfResult ' + (ok ? 'sfOk' : 'sfBad') + '">' + html + '</div>';
}

function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value == null ? '' : String(value);
    return div.innerHTML;
}

export default function (view) {
    // The saved providers are edited as plain objects and only written back on save, so a half
    // finished subscription never reaches the server.
    let providers = [];
    let config = {};

    const providerList = view.querySelector('#sfProviders');
    const providerTemplate = view.querySelector('#sfProviderTemplate');
    const filterTemplate = view.querySelector('#sfFilterTemplate');

    function applyKindVisibility(host, provider) {
        host.querySelectorAll('[data-sf-when]').forEach((section) => {
            section.hidden = section.dataset.sfWhen !== provider.Kind;
        });

        const hint = host.querySelector('[data-sf-epg-hint]');
        hint.textContent = provider.Kind === 'Xtream'
            ? 'Optional. Left blank, the panel\'s own guide is used.'
            : 'Optional. Left blank, the address named in the playlist is used when it has one.';
    }

    function updateTitle(host, provider) {
        const title = host.querySelector('[data-sf-title]');
        const source = provider.Kind === 'Xtream' ? provider.ServerUrl : provider.PlaylistUrl;
        const parts = [provider.Name || 'Unnamed'];

        const wanted = [
            provider.EnableLiveTv ? 'Live TV' : null,
            provider.EnableMovies ? 'Films' : null,
            provider.EnableSeries ? 'Shows' : null
        ].filter(Boolean);

        title.innerHTML = escapeHtml(parts[0])
            + ' <span class="sfChip">' + escapeHtml(provider.Kind === 'Xtream' ? 'Xtream' : 'M3U') + '</span>'
            + (wanted.length ? ' <span class="sfMuted">' + escapeHtml(wanted.join(' + ')) + '</span>' : '')
            + (source ? ' <span class="sfMuted">&middot; ' + escapeHtml(source.replace(/\?.*$/, '')) + '</span>' : '');
    }

    function renderFilter(container, provider, spec) {
        const fragment = filterTemplate.content.cloneNode(true);
        const root = fragment.firstElementChild;
        const filter = provider[spec.field] || (provider[spec.field] = {
            Mode: 'All',
            Categories: [],
            TitleExcludePatterns: [],
            MaxItemsPerCategory: 0
        });

        const mode = root.querySelector('[data-sf-filter-field="Mode"]');
        const picker = root.querySelector('[data-sf-category-picker]');
        const categories = root.querySelector('[data-sf-filter-field="Categories"]');
        const patterns = root.querySelector('[data-sf-filter-field="TitleExcludePatterns"]');
        const limit = root.querySelector('[data-sf-filter-field="MaxItemsPerCategory"]');
        const status = root.querySelector('[data-sf-category-status]');

        mode.value = filter.Mode || 'All';
        patterns.value = (filter.TitleExcludePatterns || []).join('\n');
        limit.value = filter.MaxItemsPerCategory || 0;

        // Show what is already selected even before the provider has been queried, so saved
        // choices are never silently dropped by a round trip through this page.
        (filter.Categories || []).forEach((id) => {
            categories.add(new Option(id, id, false, true));
        });

        function syncPicker() {
            picker.hidden = mode.value === 'All';
        }

        syncPicker();

        mode.addEventListener('change', () => {
            filter.Mode = mode.value;
            syncPicker();
        });

        categories.addEventListener('change', () => {
            filter.Categories = Array.from(categories.selectedOptions, (o) => o.value);
        });

        patterns.addEventListener('change', () => {
            filter.TitleExcludePatterns = patterns.value
                .split('\n')
                .map((line) => line.trim())
                .filter(Boolean);
        });

        limit.addEventListener('change', () => {
            filter.MaxItemsPerCategory = Math.max(0, parseInt(limit.value, 10) || 0);
        });

        root.querySelector('[data-sf-action="load-categories"]').addEventListener('click', () => {
            status.textContent = 'Loading...';

            post('Categories', provider, { contentType: spec.contentType }).then((response) => {
                if (!response.Success) {
                    status.textContent = response.Message || 'Could not load categories.';
                    return;
                }

                const selected = new Set(filter.Categories || []);
                categories.innerHTML = '';
                response.Categories.forEach((category) => {
                    const label = category.Name + ' (' + category.ItemCount.toLocaleString() + ')';
                    categories.add(new Option(label, category.Id, false, selected.has(category.Id)));
                });

                status.textContent = plural(response.Categories.length, 'category', 'categories') + ' found';
            }, () => {
                status.textContent = 'Could not reach the provider.';
            });
        });

        container.appendChild(root);
    }

    function renderProvider(provider) {
        const fragment = providerTemplate.content.cloneNode(true);
        const host = fragment.firstElementChild;
        host.dataset.sfProviderId = provider.Id;

        host.querySelectorAll('[data-sf-field]').forEach((input) => {
            const key = input.dataset.sfField;
            const value = provider[key];

            if (input.type === 'checkbox') {
                input.checked = value !== false;
            } else {
                input.value = value == null ? '' : value;
            }

            input.addEventListener('change', () => {
                if (input.type === 'checkbox') {
                    provider[key] = input.checked;
                } else if (input.type === 'number') {
                    provider[key] = Math.max(0, parseInt(input.value, 10) || 0);
                } else {
                    provider[key] = input.value;
                }

                if (key === 'Kind') {
                    applyKindVisibility(host, provider);
                }

                updateTitle(host, provider);
            });
        });

        FILTERS.forEach((spec) => {
            renderFilter(host.querySelector('[data-sf-filter="' + spec.field + '"]'), provider, spec);
        });

        const body = host.querySelector('[data-sf-body]');
        const toggleLabel = host.querySelector('[data-sf-toggle-label]');

        host.querySelector('[data-sf-action="toggle"]').addEventListener('click', () => {
            body.hidden = !body.hidden;
            toggleLabel.textContent = body.hidden ? 'Edit' : 'Hide';
        });

        host.querySelector('[data-sf-action="remove"]').addEventListener('click', () => {
            Dashboard.confirm(
                'Remove "' + (provider.Name || 'this subscription') + '"? The libraries it generated stay on disk until the next sync.',
                'Remove subscription',
                (confirmed) => {
                    if (!confirmed) {
                        return;
                    }

                    providers = providers.filter((p) => p !== provider);
                    host.remove();
                    renderEmptyState();
                });
        });

        const testResult = host.querySelector('[data-sf-test-result]');
        const testButton = host.querySelector('[data-sf-action="test"]');

        testButton.addEventListener('click', () => {
            testButton.disabled = true;
            testResult.innerHTML = '<div class="sfResult sfMuted">Contacting the provider...</div>';

            post('TestProvider', provider).then((response) => {
                testButton.disabled = false;

                if (!response.Success) {
                    setResult(testResult, false, escapeHtml(response.Message));
                    return;
                }

                const lines = [escapeHtml(response.Message)];
                lines.push([
                    plural(response.ChannelCount, 'channel'),
                    plural(response.MovieCount, 'film'),
                    plural(response.SeriesCount, 'show')
                ].join(', '));

                if (response.DiscoveredEpgUrl && !provider.EpgUrl) {
                    provider.EpgUrl = response.DiscoveredEpgUrl;
                    host.querySelector('[data-sf-field="EpgUrl"]').value = response.DiscoveredEpgUrl;
                    lines.push('The playlist named a guide address, which has been filled in for you.');
                }

                if (response.IsTrial) {
                    lines.push('This is a trial line.');
                }

                setResult(testResult, true, lines.map((l) => '<div>' + l + '</div>').join(''));
            }, () => {
                testButton.disabled = false;
                setResult(testResult, false, 'The server could not be reached.');
            });
        });

        const paste = host.querySelector('[data-sf-paste]');
        if (paste) {
            paste.addEventListener('change', () => {
                if (!applyPastedLink(provider, host, paste.value)) {
                    return;
                }

                paste.value = '';
                applyKindVisibility(host, provider);
                updateTitle(host, provider);
            });
        }

        const serverUrl = host.querySelector('[data-sf-field="ServerUrl"]');
        serverUrl.addEventListener('change', () => {
            if (!applyPastedLink(provider, host, serverUrl.value)) {
                return;
            }

            applyKindVisibility(host, provider);
            updateTitle(host, provider);
        });

        applyKindVisibility(host, provider);
        updateTitle(host, provider);
        providerList.appendChild(host);
        return host;
    }

    function renderEmptyState() {
        const existing = providerList.querySelector('[data-sf-empty]');
        if (providers.length) {
            if (existing) {
                existing.remove();
            }

            return;
        }

        if (existing) {
            return;
        }

        const empty = document.createElement('div');
        empty.className = 'sfEmpty';
        empty.setAttribute('data-sf-empty', '');
        empty.innerHTML = '<p>No subscription yet.</p>'
            + '<p class="sfMuted">Add one, paste the provider link, then choose Save and open Watch.</p>';
        providerList.appendChild(empty);
    }

    function renderProviders() {
        providerList.innerHTML = '';
        providers.forEach(renderProvider);
        renderEmptyState();
    }

    function renderStatus(status) {
        const totals = (status.Providers || []).reduce((acc, p) => ({
            channels: acc.channels + p.ChannelCount,
            movies: acc.movies + p.MovieCount,
            series: acc.series + p.SeriesCount,
            episodes: acc.episodes + p.EpisodeCount
        }), { channels: 0, movies: 0, series: 0, episodes: 0 });

        const stats = [
            ['Channels', totals.channels.toLocaleString()],
            ['Films', totals.movies.toLocaleString()],
            ['Shows', totals.series.toLocaleString()],
            ['Episodes', totals.episodes.toLocaleString()],
            ['Last sync', status.IsSyncRunning ? 'running' : formatWhen(status.LastSyncUtc)]
        ];

        view.querySelector('#sfStatus').innerHTML = stats
            .map(([label, value]) => '<div class="sfStat"><div class="sfStatValue">' + escapeHtml(value)
                + '</div><div class="sfStatLabel">' + escapeHtml(label) + '</div></div>')
            .join('');

        const detail = view.querySelector('#sfStatusDetail');

        if (status.IsSyncRunning) {
            setResult(detail, true, 'A library copy is still running. Restart Jellyfin to stop it. Watch does not need it.');
            return;
        }

        if (!status.ConfiguredProviderCount) {
            detail.innerHTML = '';
            return;
        }

        if (!status.LastSyncUtc) {
            setResult(detail, true, 'Nothing has been copied into libraries. Open Watch to play.');
            return;
        }

        const failures = (status.Providers || []).filter((p) => !p.Succeeded);

        if (failures.length) {
            setResult(detail, false, failures
                .map((p) => '<div><strong>' + escapeHtml(p.ProviderName) + ':</strong> '
                    + escapeHtml(p.Error || 'failed') + '</div>')
                .join(''));
            return;
        }

        if (status.LastError) {
            setResult(detail, false, escapeHtml(status.LastError));
            return;
        }

        setResult(detail, true, 'Everything synced. Pointer files live in <code>'
            + escapeHtml(status.LibraryRoot) + '</code>.');
    }

    function loadStatus() {
        return ApiClient.getJSON(ApiClient.getUrl('StreamFin/Status')).then(renderStatus, () => {
            view.querySelector('#sfStatusDetail').innerHTML = '';
        });
    }

    function loadConfig() {
        return ApiClient.getPluginConfiguration(PLUGIN_ID).then((loaded) => {
            config = loaded;
            providers = (loaded.Providers || []).map((p) => Object.assign({}, PROVIDER_DEFAULTS, p));
            if (!providers.length) {
                providers.push(blankProvider(1));
            }

            GLOBAL_FIELDS.forEach((field) => {
                const input = view.querySelector('#' + field.id);
                if (field.type === 'bool') {
                    input.checked = loaded[field.key] === true;
                } else {
                    input.value = loaded[field.key] == null ? '' : loaded[field.key];
                }
            });

            renderProviders();
        });
    }

    function save() {
        GLOBAL_FIELDS.forEach((field) => {
            const input = view.querySelector('#' + field.id);
            if (field.type === 'bool') {
                config[field.key] = input.checked;
            } else if (field.type === 'int') {
                config[field.key] = Math.max(0, parseInt(input.value, 10) || 0);
            } else {
                config[field.key] = input.value.trim();
            }
        });

        config.Providers = providers;

        Loading.show();
        showProgress('Saving...');

        // Our own endpoint returns JSON. Jellyfin's plugin configuration endpoint returns an empty
        // 204, which the dashboard neither accepts nor rejects, so Save looked like it did nothing.
        return post('Settings', config).then((response) => {
            Loading.hide();

            if (!response || response.Saved !== true) {
                showSaveResult(false, escapeHtml((response && response.Message) || 'The settings could not be saved.'));
                return Promise.reject(new Error('save failed'));
            }

            return response;
        }, () => {
            Loading.hide();
            showSaveResult(false, 'The settings could not be saved.');
            return Promise.reject(new Error('save failed'));
        });
    }

    function asList(value) {
        return Array.isArray(value) ? value : [];
    }

    function showProgress(html) {
        const result = view.querySelector('#sfSaveResult');
        result.innerHTML = '<div class="sfResult sfMuted">' + html + '</div>';
        result.scrollIntoView({ block: 'nearest' });
    }

    function showSaveResult(ok, html) {
        const result = view.querySelector('#sfSaveResult');
        setResult(result, ok, html);
        result.scrollIntoView({ block: 'nearest' });
    }

    function describeProvision(response) {
        const lines = [];
        const libraries = asList(response.LibrariesCreated);
        const tuners = asList(response.TunersConfigured);
        const guides = asList(response.GuidesConfigured);
        const warnings = asList(response.Warnings);

        if (libraries.length) {
            lines.push('Created ' + libraries.map(escapeHtml).join(', ') + '.');
        }

        if (tuners.length) {
            lines.push('Live TV tuner ready for ' + tuners.map(escapeHtml).join(', ') + '.');
        }

        if (guides.length) {
            lines.push('Guide attached for ' + guides.map(escapeHtml).join(', ') + '.');
        }

        if (!lines.length && response.Success) {
            lines.push('Libraries and Live TV were already set up.');
        }

        warnings.forEach((warning) => lines.push(escapeHtml(warning)));
        lines.push('The first sync is queued. Films and shows appear as it finishes.');
        return lines;
    }

    function saveAndStart() {
        const copyLibraries = view.querySelector('#sfImportLibraries').checked;
        return save().then((response) => {
            const saved = 'Saved ' + plural(response.ProviderCount, 'subscription') + '. ';
            if (!copyLibraries) {
                showSaveResult(true, saved + 'Open Watch to play. Nothing is copied into libraries.');
                return loadStatus();
            }

            showProgress(saved + 'Copying the catalog into libraries...');
            return post('Provision').then((provision) => postNoContent('Sync').then(() => {
                showSaveResult(
                    provision.Success && asList(provision.Warnings).length === 0,
                    describeProvision(provision).map((line) => '<div>' + line + '</div>').join(''));
                return loadStatus();
            }));
        }).catch(() => {
            const result = view.querySelector('#sfSaveResult');
            if (!result.querySelector('.sfBad')) {
                showSaveResult(false, 'The settings could not be saved. Test the connection, then try Save again.');
            }
        });
    }

    let saving = false;

    function onSave(event) {
        event.preventDefault();
        if (saving) {
            return;
        }

        saving = true;
        saveAndStart().then(() => {
            saving = false;
        }, () => {
            saving = false;
        });
    }

    const form = view.querySelector('#sfForm');
    form.addEventListener('submit', onSave);
    form.querySelector('button[type="submit"]').addEventListener('click', onSave);

    view.querySelector('#sfAddProvider').addEventListener('click', () => {
        const provider = blankProvider(providers.length + 1);

        providers.push(provider);
        renderEmptyState();
        const host = renderProvider(provider);
        host.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    });

    view.querySelector('#sfRefreshStatus').addEventListener('click', loadStatus);

    view.querySelector('#sfOpenWatch').addEventListener('click', () => {
        Dashboard.navigate(Dashboard.getPluginUrl('StreamFinWatch'));
    });

    view.querySelector('#sfSyncNow').addEventListener('click', () => {
        // Save first: a sync reads the stored settings, so syncing with unsaved edits on screen
        // would quietly use the old ones.
        const copyLibraries = view.querySelector('#sfImportLibraries').checked;
        save().then(() => {
            if (!copyLibraries) {
                setResult(
                    view.querySelector('#sfProvisionResult'),
                    true,
                    'Saved. Library copy is off, so nothing is imported. Open Watch to play.');
                return;
            }

            return postNoContent('Sync').then(() => {
                setResult(
                    view.querySelector('#sfProvisionResult'),
                    true,
                    'The library copy is queued. It runs as the StreamFin library sync task, where you can follow '
                    + 'its progress and cancel it.');
                return loadStatus();
            }, () => {
                setResult(view.querySelector('#sfProvisionResult'), false, 'The library copy could not be queued.');
            });
        });
    });

    view.querySelector('#sfProvision').addEventListener('click', () => {
        const result = view.querySelector('#sfProvisionResult');

        save().then(() => {
            result.innerHTML = '<div class="sfResult sfMuted">Setting things up...</div>';

            return post('Provision').then((response) => {
                const lines = [];

                if (response.LibrariesCreated.length) {
                    lines.push('Created ' + response.LibrariesCreated.map(escapeHtml).join(', ') + '.');
                }

                if (response.TunersConfigured.length) {
                    lines.push('Live TV tuner ready for '
                        + response.TunersConfigured.map(escapeHtml).join(', ') + '.');
                }

                if (response.GuidesConfigured.length) {
                    lines.push('Guide attached for '
                        + response.GuidesConfigured.map(escapeHtml).join(', ') + '.');
                }

                if (!lines.length && response.Success) {
                    lines.push('Everything was already set up.');
                }

                response.Warnings.forEach((warning) => lines.push(escapeHtml(warning)));

                setResult(
                    result,
                    response.Success && !response.Warnings.length,
                    lines.map((l) => '<div>' + l + '</div>').join(''));
            }, () => {
                setResult(result, false, 'Setup could not be run.');
            });
        });
    });

    view.addEventListener('viewshow', () => {
        Loading.show();
        Promise.all([loadConfig(), loadStatus()]).then(() => Loading.hide(), () => Loading.hide());
    });
}
