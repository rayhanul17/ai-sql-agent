// ---- AI SQL Agent chat frontend ----
// Consumes the SSE stream from /Chat/Ask and renders status, SQL (modal),
// a result table, an optional chart, and an Excel download.

const els = {
    messages: document.getElementById('messages'),
    form: document.getElementById('askForm'),
    input: document.getElementById('questionInput'),
    sendBtn: document.getElementById('sendBtn'),
    modelSelect: document.getElementById('modelSelect'),
    modelStatus: document.getElementById('modelStatus'),
    dialectSelect: document.getElementById('dialectSelect'),
    connStr: document.getElementById('connStr'),
    clearBtn: document.getElementById('clearBtn'),
    sqlModalBody: document.getElementById('sqlModalBody'),
    copySqlBtn: document.getElementById('copySqlBtn'),
};

const sqlModal = new bootstrap.Modal(document.getElementById('sqlModal'));
let chartCounter = 0;

// ---------- Model dropdown + warm-up loader ----------
async function loadModels() {
    try {
        const res = await fetch('/Chat/Models');
        const models = await res.json();
        els.modelSelect.innerHTML = '';
        models.forEach(m => {
            const opt = document.createElement('option');
            opt.value = m.id;
            opt.textContent = m.displayName + (m.isAvailable ? '' : ' (not pulled)');
            opt.disabled = !m.isAvailable;
            els.modelSelect.appendChild(opt);
        });
        const firstAvailable = models.find(m => m.isAvailable);
        if (firstAvailable) els.modelSelect.value = firstAvailable.id;
    } catch {
        els.modelStatus.innerHTML = '<span class="loading">Ollama not reachable</span>';
    }
}

// Warm the selected model into RAM; show an indeterminate loader meanwhile.
async function warmUp(model) {
    els.modelStatus.innerHTML = `<span class="loading"><span class="spinner-border spinner-border-sm"></span> Loading ${model}…</span>`;
    try {
        const res = await fetch('/Chat/Warmup', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(model),
        });
        const r = await res.json();
        if (r.success) {
            const s = r.loadDurationMs > 50 ? ` (loaded in ${(r.loadDurationMs / 1000).toFixed(1)}s)` : '';
            els.modelStatus.innerHTML = `<span class="ready"><i class="bi bi-check-circle"></i> Ready${s}</span>`;
        } else {
            els.modelStatus.innerHTML = `<span class="loading">Load failed</span>`;
        }
    } catch {
        els.modelStatus.innerHTML = `<span class="loading">Load failed</span>`;
    }
}

els.modelSelect.addEventListener('change', () => warmUp(els.modelSelect.value));

// ---------- Data source toggle ----------
els.dialectSelect.addEventListener('change', () => {
    const custom = els.dialectSelect.value !== '';
    els.connStr.disabled = !custom;
    if (!custom) els.connStr.value = '';
});

// ---------- Messages ----------
function clearEmptyState() {
    const empty = els.messages.querySelector('.empty-state');
    if (empty) empty.remove();
}

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

// ---------- Result rendering: table + actions + chart ----------
function renderResult(area, result, sql) {
    if (!result || !result.columns || result.columns.length === 0) return;

    const meta = document.createElement('div');
    meta.className = 'result-meta';
    meta.textContent = `${result.rowCount} row(s)`;
    area.appendChild(meta);

    // Table
    const wrap = document.createElement('div');
    wrap.className = 'result-wrap';
    const table = document.createElement('table');
    table.className = 'result';
    const thead = '<thead><tr>' + result.columns.map(c => `<th>${escapeHtml(c)}</th>`).join('') + '</tr></thead>';
    const rowsHtml = result.rows.map(r =>
        '<tr>' + r.map(v => `<td>${escapeHtml(v)}</td>`).join('') + '</tr>').join('');
    table.innerHTML = thead + '<tbody>' + rowsHtml + '</tbody>';
    wrap.appendChild(table);
    area.appendChild(wrap);

    // Actions: Show SQL, Excel, Chart
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
        const chartBtn = actionBtn('bi-bar-chart', 'Chart', 'btn-outline-warning');
        const chartHost = document.createElement('div');
        chartHost.className = 'chart-wrap';
        chartBtn.onclick = () => {
            if (chartHost.dataset.drawn) { chartHost.innerHTML = ''; delete chartHost.dataset.drawn; return; }
            drawChart(chartHost, result, chartable);
            chartHost.dataset.drawn = '1';
        };
        actions.appendChild(chartBtn);
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

// A result is chartable when it has at least one label (text) column and at
// least one numeric column, with a sensible number of rows. Picks the first
// numeric column as the value and the first non-numeric column as the label.
function detectChartable(result) {
    const n = result.columns.length;
    if (n < 2 || result.rowCount === 0 || result.rowCount > 50) return null;

    const colIsNumeric = i => result.rows.every(r => isNumeric(r[i]));
    let valueCol = -1, labelCol = -1;
    for (let i = 0; i < n; i++) {
        if (valueCol === -1 && colIsNumeric(i)) valueCol = i;
        else if (labelCol === -1 && !colIsNumeric(i)) labelCol = i;
    }
    // Need one of each; a label that is also the id column is fine.
    if (valueCol === -1) return null;
    if (labelCol === -1) labelCol = valueCol === 0 ? 1 : 0;
    return { labelCol, valueCol };
}

function drawChart(host, result, cfg) {
    const canvas = document.createElement('canvas');
    canvas.id = 'chart' + (++chartCounter);
    host.appendChild(canvas);
    const labels = result.rows.map(r => String(r[cfg.labelCol]));
    const data = result.rows.map(r => Number(r[cfg.valueCol]));
    new Chart(canvas, {
        type: 'bar',
        data: {
            labels,
            datasets: [{
                label: result.columns[cfg.valueCol],
                data,
                backgroundColor: 'rgba(99,102,241,.6)',
                borderColor: 'rgba(139,92,246,1)',
                borderWidth: 1,
            }],
        },
        options: {
            responsive: true, maintainAspectRatio: false,
            plugins: { legend: { labels: { color: '#e2e8f0' } } },
            scales: {
                x: { ticks: { color: '#94a3b8' }, grid: { color: '#334155' } },
                y: { ticks: { color: '#94a3b8' }, grid: { color: '#334155' } },
            },
        },
    });
}

async function downloadExcel(result, sql) {
    const payload = {
        sql,
        columns: result.columns,
        rows: result.rows,
    };
    const res = await fetch('/Chat/Export', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
    });
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'query-result.xlsx';
    a.click();
    URL.revokeObjectURL(url);
}

// ---------- SSE streaming ----------
async function ask(question) {
    addUserMessage(question);
    const ui = addBotMessage();

    const body = {
        question,
        model: els.modelSelect.value || null,
        connectionString: els.dialectSelect.value === '' ? null : (els.connStr.value || null),
        dialect: els.dialectSelect.value === '' ? null : Number(els.dialectSelect.value),
    };

    els.sendBtn.disabled = true;
    let answerText = '';
    let currentSql = null;

    try {
        const res = await fetch('/Chat/Ask', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
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
    }
}

// ---------- Wiring ----------
els.form.addEventListener('submit', e => {
    e.preventDefault();
    const q = els.input.value.trim();
    if (!q) return;
    els.input.value = '';
    ask(q);
});

els.clearBtn.addEventListener('click', () => {
    els.messages.innerHTML = `<div class="empty-state">
        <i class="bi bi-chat-square-dots"></i>
        <h5>Ask anything about your data</h5>
        <p>e.g. “Which students were absent this month?” or “Top 5 highest fees.”</p></div>`;
});

els.copySqlBtn.addEventListener('click', () => {
    navigator.clipboard.writeText(els.sqlModalBody.textContent);
    els.copySqlBtn.innerHTML = '<i class="bi bi-check"></i> Copied';
    setTimeout(() => els.copySqlBtn.innerHTML = '<i class="bi bi-clipboard"></i> Copy', 1500);
});

function escapeHtml(v) {
    if (v === null || v === undefined) return '<span class="text-secondary">NULL</span>';
    return String(v)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
function isNumeric(v) { return v !== null && v !== '' && !isNaN(Number(v)); }

// Init
loadModels().then(() => {
    if (els.modelSelect.value) warmUp(els.modelSelect.value);
});
