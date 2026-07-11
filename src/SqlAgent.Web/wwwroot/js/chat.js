// ============================================================
//  AI SQL Agent — chat frontend
//  Handles SSE streaming, result table, Show-Query modal, Excel,
//  bar/line/pie charts, dark/light theme, and a settings panel with
//  DEFERRED apply (changes take effect only on Save, with validation
//  and a full-page mask).
// ============================================================

const $ = id => document.getElementById(id);
const els = {
    messages: $('messages'), form: $('askForm'), input: $('questionInput'),
    sendBtn: $('sendBtn'), clearBtn: $('clearBtn'),
    providerSelect: $('providerSelect'), providerHint: $('providerHint'),
    modelSelect: $('modelSelect'), dialectSelect: $('dialectSelect'), connStr: $('connStr'),
    sqlModalBody: $('sqlModalBody'), copySqlBtn: $('copySqlBtn'),
    settingsToggle: $('settingsToggle'), settingsPanel: $('settingsPanel'),
    settingsBackdrop: $('settingsBackdrop'), settingsClose: $('settingsClose'),
    settingsSave: $('settingsSave'), settingsCancel: $('settingsCancel'),
    settingsReset: $('settingsReset'), settingsError: $('settingsError'),
    themeToggle: $('themeToggle'), pageMask: $('pageMask'), maskText: $('maskText'),
    activeModelBadge: $('activeModelBadge'), activeSourceBadge: $('activeSourceBadge'),
    refreshSchemaBtn: $('refreshSchemaBtn'), schemaStatus: $('schemaStatus'),
};

const sqlModal = new bootstrap.Modal($('sqlModal'));
let chartCounter = 0;

const history = [];
const MAX_HISTORY = 4;

// ---- Applied (active) settings vs. draft (in the panel, unsaved) ----
const DEFAULTS = { theme: 'dark', provider: '0', model: '', dialect: '', connStr: '' };
let applied = loadSettings();
let allModels = []; // full catalog across providers

function loadSettings() {
    try {
        const s = JSON.parse(localStorage.getItem('sqlagent.settings') || '{}');
        return { ...DEFAULTS, ...s };
    } catch { return { ...DEFAULTS }; }
}
function saveSettings() {
    localStorage.setItem('sqlagent.settings', JSON.stringify(applied));
}

// ============================================================
//  Theme
// ============================================================
function applyTheme(theme) {
    document.documentElement.setAttribute('data-bs-theme', theme);
    els.themeToggle.innerHTML = theme === 'dark'
        ? '<i class="bi bi-moon-stars"></i>' : '<i class="bi bi-sun"></i>';
}
els.themeToggle.addEventListener('click', () => {
    // Quick toggle applies immediately (theme is cosmetic, no validation needed).
    applied.theme = applied.theme === 'dark' ? 'light' : 'dark';
    applyTheme(applied.theme);
    saveSettings();
    syncDraftToPanel();
});

// ============================================================
//  Model dropdown
// ============================================================
async function loadModels() {
    try {
        const res = await fetch('/Chat/Models');
        allModels = await res.json();
    } catch {
        allModels = [];
    }
    populateModels(Number(applied.provider));
}

// Fill the Model dropdown with the chosen provider's models (cascading).
function populateModels(provider) {
    const list = allModels.filter(m => m.provider === provider);
    els.modelSelect.innerHTML = '';

    if (list.length === 0) {
        els.modelSelect.innerHTML = '<option value="">No models</option>';
    } else {
        list.forEach(m => {
            const opt = document.createElement('option');
            opt.value = m.id;
            const note = m.isAvailable ? '' : (provider === 1 ? ' (no API key)' : ' (not pulled)');
            opt.textContent = m.displayName + note;
            opt.disabled = !m.isAvailable;
            els.modelSelect.appendChild(opt);
        });
        const wanted = applied.model && list.some(m => m.id === applied.model && m.isAvailable)
            ? applied.model : (list.find(m => m.isAvailable)?.id || list[0].id);
        els.modelSelect.value = wanted;
    }

    els.providerHint.textContent = provider === 1
        ? 'Cloud (fast). Requires a Groq API key in appsettings.Development.json.'
        : 'Local Ollama. First use of a model loads it into RAM.';
}

// Provider dropdown -> repopulate models for that provider.
els.providerSelect.addEventListener('change', () =>
    populateModels(Number(els.providerSelect.value)));

// ============================================================
//  Settings panel — deferred apply
// ============================================================
function openSettings() {
    syncDraftToPanel();
    els.settingsError.classList.add('hidden');
    els.settingsPanel.classList.remove('hidden');
    els.settingsBackdrop.classList.remove('hidden');
}
function closeSettings() {
    els.settingsPanel.classList.add('hidden');
    els.settingsBackdrop.classList.add('hidden');
}

// Put the APPLIED values into the panel controls (draft starts = applied).
function syncDraftToPanel() {
    (applied.theme === 'light' ? $('themeLight') : $('themeDark')).checked = true;
    els.providerSelect.value = applied.provider;
    populateModels(Number(applied.provider));
    if (applied.model) els.modelSelect.value = applied.model;
    els.dialectSelect.value = applied.dialect;
    els.connStr.value = applied.connStr;
    els.connStr.disabled = applied.dialect === '';
}

function readDraft() {
    const theme = document.querySelector('input[name="theme"]:checked')?.value || 'dark';
    return {
        theme,
        provider: els.providerSelect.value,
        model: els.modelSelect.value || '',
        dialect: els.dialectSelect.value,
        connStr: els.dialectSelect.value === '' ? '' : els.connStr.value.trim(),
    };
}

// Correct-format connection-string templates per dialect. When a dialect is
// picked, prefill the template so users only edit the credentials / db name
// (avoids format mistakes). 0=PostgreSQL, 1=MySQL, 2=SQL Server.
const CONN_TEMPLATES = {
    '0': 'Host=localhost;Port=5432;Database=DB_NAME;Username=postgres;Password=PASSWORD',
    '1': 'Server=localhost;Port=3306;Database=DB_NAME;User ID=root;Password=PASSWORD;SslMode=None',
    '2': 'Server=localhost,1433;Database=DB_NAME;User ID=sa;Password=PASSWORD;TrustServerCertificate=True',
};

els.dialectSelect.addEventListener('change', () => {
    const v = els.dialectSelect.value;
    els.connStr.disabled = v === '';
    if (v === '') {
        els.connStr.value = '';
    } else if (!els.connStr.value.trim()) {
        // Only prefill when empty, so we don't overwrite an edited string.
        els.connStr.value = CONN_TEMPLATES[v] || '';
    }
});

els.settingsToggle.addEventListener('click', openSettings);
els.settingsClose.addEventListener('click', () => { syncDraftToPanel(); applyTheme(applied.theme); closeSettings(); });
els.settingsCancel.addEventListener('click', () => { syncDraftToPanel(); applyTheme(applied.theme); closeSettings(); });
els.settingsBackdrop.addEventListener('click', () => { syncDraftToPanel(); applyTheme(applied.theme); closeSettings(); });

els.settingsReset.addEventListener('click', () => {
    // Reset the panel to defaults (not yet applied until Save).
    $('themeDark').checked = true;
    els.providerSelect.value = '0';
    populateModels(0);
    els.dialectSelect.value = '';
    els.connStr.value = '';
    els.connStr.disabled = true;
    els.settingsError.classList.add('hidden');
});

// Live-preview theme while choosing (revert on cancel).
document.querySelectorAll('input[name="theme"]').forEach(r =>
    r.addEventListener('change', () => applyTheme(readDraft().theme)));

// Refresh schema: force re-introspect the source currently in the panel.
els.refreshSchemaBtn.addEventListener('click', async () => {
    const draft = readDraft();
    els.schemaStatus.textContent = 'Reading schema…';
    const body = draft.dialect === ''
        ? { force: true }
        : { connectionString: draft.connStr, dialect: Number(draft.dialect), force: true };
    try {
        const r = await fetch('/Chat/LoadSchema', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        }).then(x => x.json());
        els.schemaStatus.textContent = r.success
            ? `Schema loaded: ${r.tableCount} tables, ${r.columnCount} columns.`
            : `Failed: ${r.error}`;
    } catch (e) {
        els.schemaStatus.textContent = `Failed: ${e.message}`;
    }
});

// SAVE: validate model + connection, then apply with a full-page mask.
els.settingsSave.addEventListener('click', async () => {
    const draft = readDraft();
    els.settingsError.classList.add('hidden');

    showMask('Applying settings…');
    try {
        // 1) Validate the connection string if a custom source is chosen.
        if (draft.dialect !== '') {
            if (!draft.connStr) throw new Error('Please enter a connection string.');
            showMask('Testing database connection…');
            const test = await fetch('/Chat/TestConnection', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ connectionString: draft.connStr, dialect: Number(draft.dialect) }),
            }).then(r => r.json());
            if (!test.ok) throw new Error(test.error || 'Database connection failed.');
        }

        // 1b) Read + cache the full DB structure once (force re-read on Save).
        showMask('Reading database schema…');
        const schemaBody = draft.dialect === ''
            ? { force: true }
            : { connectionString: draft.connStr, dialect: Number(draft.dialect), force: true };
        const schema = await fetch('/Chat/LoadSchema', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(schemaBody),
        }).then(r => r.json());
        if (!schema.success) throw new Error(schema.error || 'Could not read database schema.');

        // 2) Warm up the model. For Ollama this is a cold RAM load; for Groq
        //    (cloud) the backend returns success instantly.
        if (draft.model) {
            const isCloud = draft.provider === '1';
            showMask(isCloud
                ? `Checking model ${draft.model}…`
                : `Loading model ${draft.model}… (larger models can take a few minutes)`);
            const warm = await fetch('/Chat/Warmup', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(draft.model),
            }).then(r => r.json());
            if (!warm.success) throw new Error(warm.error || 'Model failed to load.');
        }

        // 3) Commit the draft as applied.
        applied = draft;
        applyTheme(applied.theme);
        saveSettings();
        updateBadges();
        hideMask();
        closeSettings();
    } catch (e) {
        hideMask();
        els.settingsError.textContent = e.message;
        els.settingsError.classList.remove('hidden');
        applyTheme(applied.theme); // revert any theme preview on failure
    }
});

function showMask(text) { els.maskText.textContent = text; els.pageMask.classList.remove('hidden'); }
function hideMask() { els.pageMask.classList.add('hidden'); }

function updateBadges() {
    const providerName = applied.provider === '1' ? 'Groq' : 'Ollama';
    els.activeModelBadge.innerHTML = `<i class="bi bi-cpu"></i> ${providerName}: ${applied.model || 'default'}`;
    const src = applied.dialect === '' ? 'Demo DB'
        : ['PostgreSQL', 'MySQL', 'SQL Server'][Number(applied.dialect)] + ' (custom)';
    els.activeSourceBadge.innerHTML = `<i class="bi bi-database"></i> ${src}`;
}

// ============================================================
//  Messages
// ============================================================
function clearEmptyState() { els.messages.querySelector('.empty-state')?.remove(); }

function addUserMessage(text) {
    clearEmptyState();
    const div = document.createElement('div');
    div.className = 'msg user';
    div.innerHTML = `<div class="msg-avatar"><i class="bi bi-person"></i></div>
        <div class="msg-body"><div class="bubble"></div></div>`;
    div.querySelector('.bubble').textContent = text;
    els.messages.appendChild(div);
    scrollDown();
}

function addBotMessage() {
    const div = document.createElement('div');
    div.className = 'msg bot';
    div.innerHTML = `<div class="msg-avatar"><i class="bi bi-robot"></i></div>
        <div class="msg-body"><div class="bubble">
            <div class="status-line"></div>
            <div class="result-area"></div>
            <div class="answer typing"></div>
        </div></div>`;
    els.messages.appendChild(div);
    scrollDown();
    return {
        status: div.querySelector('.status-line'),
        result: div.querySelector('.result-area'),
        answer: div.querySelector('.answer'),
    };
}

function scrollDown() { els.messages.scrollTop = els.messages.scrollHeight; }

// ============================================================
//  Result rendering: table + actions + charts
// ============================================================
function renderResult(area, result, sql) {
    if (!result || !result.columns || result.columns.length === 0) return;

    const meta = document.createElement('div');
    meta.className = 'result-meta';
    meta.textContent = `${result.rowCount} row(s)`;
    area.appendChild(meta);

    const wrap = document.createElement('div');
    wrap.className = 'result-wrap';
    const table = document.createElement('table');
    table.className = 'result';
    const thead = '<thead><tr>' + result.columns.map(c => `<th>${escapeHtml(c)}</th>`).join('') + '</tr></thead>';
    const rowsHtml = result.rows.map(r =>
        '<tr>' + r.map(v => {
            const s = v === null || v === undefined ? '' : String(v);
            return `<td title="${escapeAttr(s)}">${escapeHtml(v)}</td>`;
        }).join('') + '</tr>').join('');
    table.innerHTML = thead + '<tbody>' + rowsHtml + '</tbody>';
    wrap.appendChild(table);
    area.appendChild(wrap);

    const actions = document.createElement('div');
    actions.className = 'result-actions';

    const sqlBtn = actionBtn('bi-code-square', 'Show Query', 'btn-outline-info');
    sqlBtn.onclick = () => { els.sqlModalBody.textContent = sql; sqlModal.show(); };
    actions.appendChild(sqlBtn);

    const xlsBtn = actionBtn('bi-file-earmark-excel', 'Excel', 'btn-outline-success');
    xlsBtn.onclick = () => downloadExcel(result, sql);
    actions.appendChild(xlsBtn);

    const chartable = detectChartable(result);
    if (chartable) {
        const chartHost = document.createElement('div');
        chartHost.className = 'chart-wrap';
        let current = null;

        const types = document.createElement('span');
        types.className = 'chart-types';
        ['bar', 'line', 'pie'].forEach(type => {
            const icon = { bar: 'bi-bar-chart', line: 'bi-graph-up', pie: 'bi-pie-chart' }[type];
            const b = actionBtn(icon, type[0].toUpperCase() + type.slice(1), 'btn-outline-warning');
            b.onclick = () => {
                if (current) current.destroy();
                chartHost.innerHTML = '';
                current = drawChart(chartHost, result, chartable, type);
            };
            types.appendChild(b);
        });
        actions.appendChild(types);
        area.appendChild(actions);
        area.appendChild(chartHost);
    } else {
        area.appendChild(actions);
    }
}

function actionBtn(icon, label, cls) {
    const b = document.createElement('button');
    b.className = `btn btn-sm ${cls}`;
    b.innerHTML = `<i class="bi ${icon}"></i> ${label}`;
    return b;
}

// Chartable = has a label (text) column and a numeric column, reasonable row count.
function detectChartable(result) {
    const n = result.columns.length;
    if (n < 2 || result.rowCount === 0 || result.rowCount > 50) return null;
    const colIsNumeric = i => result.rows.every(r => isNumeric(r[i]));
    let valueCol = -1, labelCol = -1;
    for (let i = 0; i < n; i++) {
        if (valueCol === -1 && colIsNumeric(i)) valueCol = i;
        else if (labelCol === -1 && !colIsNumeric(i)) labelCol = i;
    }
    if (valueCol === -1) return null;
    if (labelCol === -1) labelCol = valueCol === 0 ? 1 : 0;
    return { labelCol, valueCol };
}

const PALETTE = ['#6366f1', '#8b5cf6', '#ec4899', '#f59e0b', '#10b981',
    '#3b82f6', '#ef4444', '#14b8a6', '#a855f7', '#eab308'];

function drawChart(host, result, cfg, type) {
    const canvas = document.createElement('canvas');
    canvas.id = 'chart' + (++chartCounter);
    host.appendChild(canvas);
    const labels = result.rows.map(r => String(r[cfg.labelCol]));
    const data = result.rows.map(r => Number(r[cfg.valueCol]));
    const isPie = type === 'pie';
    const colors = labels.map((_, i) => PALETTE[i % PALETTE.length]);
    const tick = getComputedStyle(document.body).getPropertyValue('--text-dim') || '#94a3b8';

    return new Chart(canvas, {
        type,
        data: {
            labels,
            datasets: [{
                label: result.columns[cfg.valueCol],
                data,
                backgroundColor: isPie ? colors : 'rgba(99,102,241,.6)',
                borderColor: isPie ? '#0f172a' : 'rgba(139,92,246,1)',
                borderWidth: isPie ? 2 : 1,
                fill: type === 'line' ? false : true,
                tension: .3,
            }],
        },
        options: {
            responsive: true, maintainAspectRatio: false,
            plugins: { legend: { display: isPie, labels: { color: tick } } },
            scales: isPie ? {} : {
                x: { ticks: { color: tick }, grid: { color: 'rgba(148,163,184,.15)' } },
                y: { ticks: { color: tick }, grid: { color: 'rgba(148,163,184,.15)' } },
            },
        },
    });
}

async function downloadExcel(result, sql) {
    const res = await fetch('/Chat/Export', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sql, columns: result.columns, rows: result.rows }),
    });
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = 'query-result.xlsx'; a.click();
    URL.revokeObjectURL(url);
}

// ============================================================
//  SSE streaming
// ============================================================
async function ask(question) {
    addUserMessage(question);
    const ui = addBotMessage();

    const body = {
        question,
        provider: Number(applied.provider),
        model: applied.model || null,
        connectionString: applied.dialect === '' ? null : (applied.connStr || null),
        dialect: applied.dialect === '' ? null : Number(applied.dialect),
        history: history.slice(-MAX_HISTORY),
    };

    els.sendBtn.disabled = true;
    els.sendBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';
    let answerText = '';
    let currentSql = null;

    try {
        const res = await fetch('/Chat/Ask', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            const parts = buffer.split('\n\n');
            buffer = parts.pop();
            for (const part of parts) {
                const line = part.trim();
                if (!line.startsWith('data:')) continue;
                const chunk = JSON.parse(line.slice(5).trim());

                if (chunk.type === 'status') {
                    ui.status.innerHTML = `<span class="spinner-border spinner-border-sm"></span> ${escapeHtml(chunk.content)}`;
                } else if (chunk.type === 'sql') {
                    currentSql = chunk.content;
                } else if (chunk.type === 'rows') {
                    renderResult(ui.result, chunk.data, currentSql);
                    scrollDown();
                } else if (chunk.type === 'token') {
                    answerText += chunk.content;
                    ui.answer.textContent = answerText;
                    scrollDown();
                } else if (chunk.type === 'done') {
                    ui.status.innerHTML = '';
                    ui.answer.classList.remove('typing');
                    if (currentSql) history.push({ question, sql: currentSql });
                } else if (chunk.type === 'error') {
                    ui.status.innerHTML = '';
                    ui.answer.classList.remove('typing');
                    ui.answer.innerHTML = `<div class="text-danger"><i class="bi bi-exclamation-triangle"></i> ${escapeHtml(chunk.content)}</div>`;
                }
            }
        }
    } catch (e) {
        ui.status.innerHTML = '';
        ui.answer.classList.remove('typing');
        ui.answer.innerHTML = `<div class="text-danger">Connection error: ${escapeHtml(e.message)}</div>`;
    } finally {
        els.sendBtn.disabled = false;
        els.sendBtn.innerHTML = '<i class="bi bi-send"></i>';
        els.input.focus();
    }
}

// ============================================================
//  Wiring
// ============================================================
els.form.addEventListener('submit', e => {
    e.preventDefault();
    const q = els.input.value.trim();
    if (!q) return;
    els.input.value = '';
    ask(q);
});

els.clearBtn.addEventListener('click', () => {
    history.length = 0;
    els.messages.innerHTML = `<div class="empty-state">
        <i class="bi bi-chat-square-dots"></i>
        <h5>Ask anything about your data</h5>
        <p>e.g. “How many students are in each class?” or “Top 5 highest fees.”</p></div>`;
});

els.copySqlBtn.addEventListener('click', () => {
    navigator.clipboard.writeText(els.sqlModalBody.textContent);
    els.copySqlBtn.innerHTML = '<i class="bi bi-check"></i> Copied';
    setTimeout(() => els.copySqlBtn.innerHTML = '<i class="bi bi-clipboard"></i> Copy', 1500);
});

function escapeHtml(v) {
    if (v === null || v === undefined) return '<span class="text-secondary">NULL</span>';
    return String(v).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
function escapeAttr(s) {
    return String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;')
        .replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
function isNumeric(v) { return v !== null && v !== '' && !isNaN(Number(v)); }

// ============================================================
//  Init
// ============================================================
applyTheme(applied.theme);
loadModels().then(() => {
    // If no model chosen yet, adopt the current provider's first available.
    if (!applied.model) applied.model = els.modelSelect.value || '';
    updateBadges();
    // Silently warm the model for the active provider (Groq no-ops server-side).
    if (applied.model) {
        fetch('/Chat/Warmup', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(applied.model),
        }).catch(() => {});
    }
});
