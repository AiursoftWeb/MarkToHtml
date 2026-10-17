window.MathJax = {
    tex: {
        inlineMath: [['$', '$'], ['\\(', '\\)']],
        displayMath: [['$$', '$$'], ['\\[', '\\]']]
    },
    svg: { fontCache: 'global' }
};

document.addEventListener('DOMContentLoaded', async () => {
    const form = document.getElementById('markdown-form');
    if (window.jQuery?.validator) window.jQuery.validator.setDefaults({ ignore: [] });
    window.require?.config?.({ paths: { vs: form.dataset.monacoPath } });

    await window.AiursoftMarkdownUi.createMarkdownEditor({
        editorContainer: document.getElementById('markdown-editor-container'),
        textarea: document.getElementById('markdown-editor'),
        previewContainer: document.getElementById('preview-content'),
        editorPane: document.getElementById('editor-column'),
        previewPane: document.getElementById('preview-column'),
        form,
        loadMonaco: () => window.AiursoftMarkdownUi.loadMonacoFromAmd(),
        theme: form.dataset.editorTheme,
        mermaidTheme: form.dataset.mermaidTheme,
        uploadUrl: form.dataset.imageUploadUrl,
        initialViewMode: 'split',
        viewModeStorageKey: 'admin-markdown-view-mode',
        viewModeControls: [...form.querySelectorAll('input[name="view-mode"]')]
            .map(element => ({ element, mode: element.value })),
        onSave: () => form.requestSubmit(),
        imageUploadOptions: {
            onError: error => {
                console.error('Image upload failed:', error);
                const banner = document.getElementById('upload-error-banner');
                banner.classList.add('show');
                setTimeout(() => banner.classList.remove('show'), 2000);
            }
        },
        onInitializationError: error => console.error('Markdown editor initialization error:', error),
        onPreviewError: error => console.error('Markdown preview error:', error)
    });
});
