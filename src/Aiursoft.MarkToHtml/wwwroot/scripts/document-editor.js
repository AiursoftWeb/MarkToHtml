(() => {
    const config = document.getElementById('markdown-form');
    const labels = {saving: 'Saving…', saved: 'Saved', unsaved: 'Unsaved changes', failed: 'Save failed. Try again.', play: 'Read Aloud', pause: 'Pause', resume: 'Resume', copied: 'Copied!'};
    const localized = key => document.querySelector(`#editor-loc [data-key="${key}"]`)?.textContent ?? labels[key];
    function iconLabel(icon, key) {
        const wrapper = document.createElement('span');
        const image = document.createElement('i'); image.className = 'align-middle'; image.dataset.lucide = icon;
        const text = document.createElement('span'); text.className = 'd-none d-xxl-inline'; text.textContent = localized(key);
        wrapper.append(image, ' ', text);
        return wrapper.innerHTML;
    }
    window.require?.config?.({paths: {vs: '/node_modules/monaco-editor/min/vs'}});
    window.MathJax = {
        tex: {
            inlineMath: [['$', '$'], ['\\(', '\\)']],
            displayMath: [['$$', '$$'], ['\\[', '\\]']],
        },
        svg: {
            fontCache: 'global'
        }
    };

    document.addEventListener("DOMContentLoaded", async function () {
        // ── AI Assistant: always init widget (it renders unconditionally) ──
        const _tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const _antiForgeryToken = _tokenInput ? _tokenInput.value : '';
        AgentChat.init(_antiForgeryToken, config.dataset.documentId, null, null);

        const aiButton = document.getElementById('ai-assistant-button');
        if (aiButton) {
            aiButton.addEventListener('click', function() {
                AgentChat.toggleWidget();
            });
        }

        let markdownEditorInstance;
        let markdownEditorController;
        const markdownEditorElement = document.getElementById('markdown-editor');
        const monacoTheme = config.dataset.darkMode === 'true' ? 'vs-dark' : 'vs';
        let isDirty = false, saving = false, revision = 0;
        const saveStatus = document.getElementById('save-status');
        function showSaveStatus(state) {
            if (!saveStatus) return;
            saveStatus.textContent = localized(state);
            saveStatus.dataset.state = state;
            saveStatus.classList.toggle('text-danger', state === 'failed');
            saveStatus.classList.toggle('text-muted', state !== 'failed');
            saveStatus.setAttribute('aria-live', state === 'failed' ? 'assertive' : 'polite');
        }
        function markDirty() {
            revision++;
            isDirty = true;
            if (!saving) showSaveStatus('unsaved');
            updateSaveButtonState();
        }
        const isAuthenticated = config.dataset.authenticated === 'true';
        const isEditing = config.dataset.editing === 'true';
        const saveButton = document.getElementById('save-button');

        function updateSaveButtonState() {
            if (!saveButton || !isAuthenticated) return;
            if (!saving && (!isEditing || isDirty)) {
                saveButton.disabled = false;
                saveButton.classList.replace('btn-secondary', 'btn-primary');
            } else {
                saveButton.disabled = true;
                saveButton.classList.replace('btn-primary', 'btn-secondary');
            }
        }

        async function doSave() {
            if (!isAuthenticated || saving || (isEditing && !isDirty)) return;
            saving = true;
            showSaveStatus('saving');
            updateSaveButtonState();
            markdownEditorElement.value = markdownEditorController.getValue();
            if (!isEditing) {
                isDirty = false;
                config.submit();
                return;
            }

            const savedRevision = revision;
            const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
            const formData = new FormData();
            formData.append('InputMarkdown', markdownEditorElement.value);
            formData.append('Title', document.getElementById('Title')?.value || '');
            formData.append('DocumentId', config.dataset.documentId);
            formData.append('__RequestVerificationToken', tokenInput ? tokenInput.value : '');
            try {
                const response = await fetch(config.dataset.saveUrl, { method: 'POST', body: formData });
                if (!response.ok) throw new Error('Save failed');
                const data = await response.json();
                if (!data.success) throw new Error('Save not confirmed');
                isDirty = revision !== savedRevision;
                showSaveStatus(isDirty ? 'unsaved' : 'saved');
            } catch (error) {
                console.error('Quick save failed:', error);
                showSaveStatus('failed');
            } finally {
                saving = false;
                updateSaveButtonState();
            }
        }

        if (markdownEditorElement) {
            markdownEditorController = await window.AiursoftMarkdownUi.createMarkdownEditor({
                editorContainer: document.getElementById('markdown-editor-container'),
                textarea: markdownEditorElement,
                previewContainer: document.getElementById('printable-area'),
                editorPane: document.getElementById('editor-column'),
                previewPane: document.getElementById('preview-column'),
                form: document.getElementById('markdown-form'),
                loadMonaco: function () { return window.AiursoftMarkdownUi.loadMonacoFromAmd(); },
                theme: monacoTheme,
                initialViewMode: 'split',
                viewModeStorageKey: 'markdown-view-mode',
                uploadUrl: isAuthenticated ? config.dataset.imageUploadUrl : undefined,
                mermaidTheme: config.dataset.darkMode === 'true' ? 'dark' : 'default',
                onSave: doSave,
                onChange: function () {
                    if (isAuthenticated) {
                        markDirty();
                    }
                },
                onPreviewRendered: function (markdown) {
                    const htmlOutput = document.getElementById('html-output');
                    if (htmlOutput) {
                        htmlOutput.value = window.AiursoftMarkdownUi.renderMarkdown(markdown);
                    }
                },
                imageUploadOptions: {
                    onError: function (error) {
                        console.error('Image upload failed:', error);
                        const banner = document.getElementById('upload-error-banner');
                        if (!banner) return;
                        banner.classList.add('show');
                        setTimeout(function () { banner.classList.remove('show'); }, 2000);
                    }
                },
                onInitializationError: function (error) {
                    console.error('Markdown editor initialization error:', error);
                },
                onPreviewError: function (error) {
                    console.error('Markdown preview error:', error);
                }
            });
            markdownEditorInstance = markdownEditorController.editor;
            updateSaveButtonState();
            showSaveStatus(isEditing ? 'saved' : 'unsaved');

            if (isAuthenticated) {
                window.addEventListener('beforeunload', function (event) {
                    if (!isDirty) return;
                    event.preventDefault();
                    event.returnValue = '';
                });
                const titleInput = document.getElementById('Title');
                if (titleInput) {
                    titleInput.addEventListener('input', function () {
                        markDirty();
                    });
                }
            }
            if (saveButton) saveButton.addEventListener('click', doSave);
        }


        // --- View Mode Switcher ---
        const editorCol = document.getElementById('editor-column');
        const previewCol = document.getElementById('preview-column');
        const formRoot = document.getElementById('markdown-form');
        const radiosDesktop = document.querySelectorAll('input[name="view-mode-desktop"]');
        const radiosMobile = document.querySelectorAll('input[name="view-mode-mobile"]');

        function isMobile() {
            return window.innerWidth < 992;
        }

        function setViewMode(mode, fromMobile) {
            if (!editorCol || !previewCol || !formRoot) return;

            if (isMobile() && mode === 'split') {
                mode = 'editor';
            }

            localStorage.setItem('markdown-view-mode', mode);
            if (markdownEditorController) {
                void markdownEditorController.setViewMode(mode);
            }

            // Sync radio buttons
            const desktopRadio = document.querySelector(`input[name="view-mode-desktop"][value="${mode}"]`);
            if (desktopRadio) desktopRadio.checked = true;
            const mobileRadio = document.querySelector(`input[name="view-mode-mobile"][value="${mode}"]`);
            if (mobileRadio) mobileRadio.checked = true;

            // Apply classes for desktop
            if (!isMobile()) {
                formRoot.classList.remove('view-mode-editor', 'view-mode-preview');
                if (mode === 'split') {
                    editorCol.classList.remove('d-none', 'col-lg-12');
                    editorCol.classList.add('col-lg-6');
                    previewCol.classList.remove('d-none', 'col-lg-12');
                    previewCol.classList.add('col-lg-6');
                } else if (mode === 'editor') {
                    editorCol.classList.remove('d-none', 'col-lg-6');
                    editorCol.classList.add('col-lg-12');
                    previewCol.classList.add('d-none');
                } else if (mode === 'preview') {
                    editorCol.classList.add('d-none');
                    previewCol.classList.remove('d-none', 'col-lg-6');
                    previewCol.classList.add('col-lg-12');
                }
            }

            // Apply classes for mobile
            if (isMobile()) {
                editorCol.classList.remove('col-lg-6', 'col-lg-12', 'd-none');
                previewCol.classList.remove('col-lg-6', 'col-lg-12', 'd-none');
                formRoot.classList.remove('view-mode-editor', 'view-mode-preview');
                formRoot.classList.add(`view-mode-${mode}`);
            }

        }

        function applyInitialViewMode() {
            const savedMode = localStorage.getItem('markdown-view-mode') || 'split';
            setViewMode(savedMode);
        }

        radiosDesktop.forEach(r => r.addEventListener('change', (e) => setViewMode(e.target.value, false)));
        radiosMobile.forEach(r => r.addEventListener('change', (e) => setViewMode(e.target.value, true)));
        window.addEventListener('resize', applyInitialViewMode);
        applyInitialViewMode();

        if ($.validator) $.validator.setDefaults({ ignore: [] });

        // --- Copy & Print Utils ---
        const copyButton = document.getElementById('copy-button');
        const htmlOutputElementForCopy = document.getElementById('html-output');
        if (copyButton && htmlOutputElementForCopy) {
            copyButton.addEventListener('click', function () {
                const val = htmlOutputElementForCopy.value;
                if (!val) return;
                navigator.clipboard.writeText(val).then(
                () => { let h = copyButton.innerHTML; copyButton.innerHTML = iconLabel('check', 'copied'); setTimeout(() => copyButton.innerHTML = h, 2000); },
                (err) => console.error(err)
                );
            });
        }
        const themeBtn = document.querySelector('.js-theme-toggle');
        if (themeBtn) themeBtn.addEventListener('click', () => setTimeout(() => document.getElementById('markdown-form').submit(), 1000));
        const printBtn = document.getElementById('print-button');
        if (printBtn) {
            printBtn.addEventListener('click', async () => {
                const isEditing = config.dataset.editing === 'true';
                if (isEditing) {
                    const printUrl = config.dataset.printUrl;
                    window.open(printUrl, '_blank');
                } else {
                    printBtn.disabled = true;
                    try {
                        await markdownEditorController.refreshPreview();
                        await window.AiursoftMarkdownUi.printMarkdown({
                            container: '#printable-area',
                            mermaidTheme: config.dataset.darkMode === 'true' ? 'dark' : 'default'
                        });
                    } catch (error) {
                        console.error('Markdown printing failed:', error);
                    } finally {
                        printBtn.disabled = false;
                    }
                }
            });
        }

        // --- Read Aloud (TTS) ---
        new TtsPlayer({
            button: document.getElementById('read-aloud-button'),
            voiceSelect: document.getElementById('voice-select'),
            getText: () => (document.getElementById('printable-area')?.textContent || '').trim(),
            labels: {
                play: iconLabel('volume-2', 'play'),
                pause: iconLabel('pause', 'pause'),
                resume: iconLabel('volume-2', 'resume')
            },
            playingClass: 'btn-primary',
            idleClass: 'btn-white'
        });

        // --- Public Link Copy ---
        const cpLink = document.getElementById('copy-link-button');
        const pubInp = document.getElementById('public-link');
        if (cpLink && pubInp) {
            cpLink.addEventListener('click', () => {
                pubInp.select();
                navigator.clipboard.writeText(pubInp.value).then(() => {
                    let h = cpLink.innerHTML; cpLink.innerHTML = `<i class="align-middle" data-lucide="check"></i>`;
                    setTimeout(() => cpLink.innerHTML = h, 2000);
                });
            });
        }

        // ── AI Assistant: wire up Monaco-dependent callbacks ──
        if (typeof AgentChat !== 'undefined' && AgentChat.updateCallbacks) {
            AgentChat.updateCallbacks(
            function() { return markdownEditorController.getValue(); },
            function(content) {
                markdownEditorController.setValue(content);
                isDirty = false; // agent already saved to DB
                updateSaveButtonState();
                showSaveStatus('saved');
                void markdownEditorController.refreshPreview();
            }
            );
        }
    });

})();
