(() => {
    const button = document.getElementById('copy-markdown-button');
    const source = document.getElementById('markdown-viewer');
    const status = document.getElementById('source-copy-status');
    const labels = document.getElementById('source-copy-text');
    if (!button || !source || !status || !labels) return;
    const text = (key, fallback) => labels.querySelector(`[data-key="${key}"]`)?.textContent.trim() || fallback;

    button.addEventListener('click', async () => {
        if (button.disabled) return;
        const restoreFocus = document.activeElement === button;
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        status.textContent = '';
        status.hidden = true;
        let copied = false;
        try {
            if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
            await navigator.clipboard.writeText(source.value);
            copied = true;
            status.textContent = text('copied', 'Source copied.');
        } catch {
            source.focus({ preventScroll: true });
            source.select();
            status.textContent = text('manual', 'Could not copy. Copy the selected source manually.');
        } finally {
            button.disabled = false;
            button.removeAttribute('aria-busy');
            status.hidden = false;
            if (copied && restoreFocus && document.activeElement === document.body) {
                button.focus({ preventScroll: true });
            }
        }
    });
})();
