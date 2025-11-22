// File: wwwroot/js/admin-messages.js
(function () {
    const modalEl = document.getElementById('flagModal');
    let modal;
    if (modalEl && window.bootstrap) modal = new bootstrap.Modal(modalEl);

    async function openFlagModal(url) {
        const res = await fetch(url, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        });
        const html = await res.text();
        document.querySelector('#flagModal .modal-content').innerHTML = html;
        if (!modal) modal = new bootstrap.Modal(modalEl);
        modal.show();

        const form = document.getElementById('flag-form');
        if (!form) return;

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const fd = new FormData(form); // includes __RequestVerificationToken

            try {
                const r = await fetch(form.action, {
                    method: 'POST',
                    headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' },
                    body: fd,
                    credentials: 'same-origin'
                });

                const ct = r.headers.get('content-type') || '';
                if (!ct.includes('application/json')) {
                    // server didn’t return JSON → do a full post so action still works
                    form.submit();
                    return;
                }

                const data = await r.json();
                if (!data.ok) { alert(data.error || 'Failed.'); return; }
                window.location.reload();
            } catch {
                // network/parse issues → fallback to normal submit
                form.submit();
            }
        });
    }

    document.addEventListener('click', (e) => {
        const t = e.target.closest('[data-flag-modal]');
        if (t) {
            e.preventDefault();
            openFlagModal(t.getAttribute('data-flag-modal'));
        }
    });

    // Unblock (AJAX with safe fallback)
    document.addEventListener('click', async (e) => {
        const t = e.target.closest('[data-unflag]');
        if (!t) return;
        e.preventDefault();
        if (!confirm('Unblock this conversation?')) return;

        const url = t.getAttribute('data-unflag');
        // Try AJAX first
        try {
            const r = await fetch(url, {
                method: 'POST',
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' },
                credentials: 'same-origin',
                body: new URLSearchParams({
                    __RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                })
            });

            const ct = r.headers.get('content-type') || '';
            if (!ct.includes('application/json')) {
                // fallback to full POST
                const f = document.createElement('form');
                f.method = 'post'; f.action = url;
                const tok = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                if (tok) {
                    const h = document.createElement('input');
                    h.type = 'hidden'; h.name = '__RequestVerificationToken'; h.value = tok;
                    f.appendChild(h);
                }
                document.body.appendChild(f); f.submit();
                return;
            }

            const data = await r.json();
            if (!data.ok) { alert(data.error || 'Failed.'); return; }
            window.location.reload();
        } catch {
            // Fallback via full POST
            const f = document.createElement('form');
            f.method = 'post'; f.action = url;
            const tok = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (tok) {
                const h = document.createElement('input');
                h.type = 'hidden'; h.name = '__RequestVerificationToken'; h.value = tok;
                f.appendChild(h);
            }
            document.body.appendChild(f); f.submit();
        }
    });
})();
