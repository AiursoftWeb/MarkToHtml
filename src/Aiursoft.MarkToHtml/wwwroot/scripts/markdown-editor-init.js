(function() {
    window.initMarkdownEditor = function(config) {
        require(['vs/editor/editor.main'], function () {
            let markdownEditorInstance, htmlOutputInstance;
            const markdownEditorElement = document.getElementById('markdown-editor');
            const htmlOutputElement = document.getElementById('html-output');
            const monacoTheme = config.isDarkMode ? "vs-dark" : "vs";

            // --- Monaco Editor Setup ---
            if (markdownEditorElement) {
                markdownEditorInstance = monaco.editor.create(document.getElementById('markdown-editor-container'), {
                    value: markdownEditorElement.value,
                    language: 'markdown',
                    theme: monacoTheme,
                    automaticLayout: true,
                    wordWrap: 'on',
                    'semanticHighlighting.enabled': true
                });

                // --- Unsaved Changes Warning ---
                let isDirty = false;
                const isAuthenticated = config.isAuthenticated;

                if (isAuthenticated) {
                    window.addEventListener('beforeunload', function (e) {
                        if (isDirty) {
                            e.preventDefault();
                            e.returnValue = '';
                        }
                    });

                    const form = document.getElementById('markdown-form');
                    if (form) {
                        form.addEventListener('submit', () => {
                            isDirty = false;
                        });
                    }

                    const titleInput = document.getElementById('Title');
                    if (titleInput) {
                        titleInput.addEventListener('input', () => {
                            isDirty = true;
                        });
                    }
                }

                markdownEditorInstance.onDidChangeModelContent(() => {
                    const currentValue = markdownEditorInstance.getValue();
                    markdownEditorElement.value = currentValue;
                    markdownEditorElement.dispatchEvent(new Event('change'));

                    if (isAuthenticated) {
                        isDirty = true;
                    }
                });

                // --- Markdown All In One Features ---

                // 1. Enter Key Handler for List Continuation
                markdownEditorInstance.onKeyDown((e) => {
                    if (e.keyCode === monaco.KeyCode.Enter && !e.ctrlKey && !e.altKey && !e.metaKey && !e.shiftKey) {
                        const position = markdownEditorInstance.getPosition();
                        const model = markdownEditorInstance.getModel();
                        const lineContent = model.getLineContent(position.lineNumber);

                        const unorderedMatch = lineContent.match(/^(\s*)([\-\*\+])\s(.*)$/);
                        const orderedMatch = lineContent.match(/^(\s*)(\d+)(\.)\s(.*)$/);

                        if (unorderedMatch) {
                            e.preventDefault();
                            e.stopPropagation();
                            const indent = unorderedMatch[1];
                            const marker = unorderedMatch[2];
                            const content = unorderedMatch[3];

                            if (!content.trim()) {
                                markdownEditorInstance.executeEdits('markdown-list', [{
                                    range: new monaco.Range(position.lineNumber, 1, position.lineNumber, lineContent.length + 1),
                                    text: '',
                                    forceMoveMarkers: true
                                }]);
                            } else {
                                const insertText = `
${indent}${marker} `;
                                markdownEditorInstance.executeEdits('markdown-list', [{
                                    range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
                                    text: insertText,
                                    forceMoveMarkers: true
                                }]);
                                markdownEditorInstance.setPosition({
                                    lineNumber: position.lineNumber + 1,
                                    column: insertText.length
                                });
                                markdownEditorInstance.revealPositionInCenterIfOutsideViewport(markdownEditorInstance.getPosition());
                            }
                        } else if (orderedMatch) {
                            e.preventDefault();
                            e.stopPropagation();
                            const indent = orderedMatch[1];
                            const number = parseInt(orderedMatch[2]);
                            const dot = orderedMatch[3];
                            const content = orderedMatch[4];

                            if (!content.trim()) {
                                markdownEditorInstance.executeEdits('markdown-list', [{
                                    range: new monaco.Range(position.lineNumber, 1, position.lineNumber, lineContent.length + 1),
                                    text: '',
                                    forceMoveMarkers: true
                                }]);
                            } else {
                                const edits = [];
                                const nextNumber = number + 1;
                                const insertText = `
${indent}${nextNumber}${dot} `;

                                // Insert the new list item
                                edits.push({
                                    range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
                                    text: insertText,
                                    forceMoveMarkers: true
                                });

                                // Renumber subsequent list items
                                for (let i = position.lineNumber + 1; i <= model.getLineCount(); i++) {
                                    const nextLine = model.getLineContent(i);
                                    const nextLineMatch = nextLine.match(/^(\s*)(\d+)(\.)\s(.*)$/);
                                    if (nextLineMatch && nextLineMatch[1] === indent) {
                                        const oldNum = parseInt(nextLineMatch[2]);
                                        const newNum = oldNum + 1;
                                        const newLineText = `${indent}${newNum}${dot} ${nextLineMatch[4]}`;
                                        edits.push({
                                            range: new monaco.Range(i, 1, i, nextLine.length + 1),
                                            text: newLineText
                                        });
                                    } else {
                                        break; // Not a list item or different indentation
                                    }
                                }

                                markdownEditorInstance.executeEdits('markdown-list-renumber', edits);
                                markdownEditorInstance.setPosition({
                                    lineNumber: position.lineNumber + 1,
                                    column: insertText.length
                                });
                                markdownEditorInstance.revealPositionInCenterIfOutsideViewport(markdownEditorInstance.getPosition());
                            }
                        }
                    }
                });

                // 2. Shortcuts

                function wrapSelection(startStr, endStr) {
                    const selection = markdownEditorInstance.getSelection();
                    const model = markdownEditorInstance.getModel();
                    const text = model.getValueInRange(selection);

                    markdownEditorInstance.executeEdits('markdown-shortcut', [{
                        range: selection,
                        text: `${startStr}${text}${endStr}`,
                        forceMoveMarkers: true
                    }]);
                }

                markdownEditorInstance.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyB, () => wrapSelection('**', '**'));
                markdownEditorInstance.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyI, () => wrapSelection('*', '*'));

                markdownEditorInstance.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyK, () => {
                    const selection = markdownEditorInstance.getSelection();
                    if (selection.startLineNumber !== selection.endLineNumber) {
                        wrapSelection('```
', '
```');
                    } else {
                        wrapSelection('`', '`');
                    }
                });

                markdownEditorInstance.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyL, () => wrapSelection('[', '](url)'));

                for (let i = 1; i <= 6; i++) {
                    markdownEditorInstance.addCommand(monaco.KeyMod.CtrlCmd | (monaco.KeyCode.Digit0 + i), () => {
                        const position = markdownEditorInstance.getPosition();
                        const model = markdownEditorInstance.getModel();
                        const lineContent = model.getLineContent(position.lineNumber);

                        const cleanContent = lineContent.replace(/^#+\s*/, '');
                        const hashes = '#'.repeat(i) + ' ';

                        markdownEditorInstance.executeEdits('markdown-heading', [{
                            range: new monaco.Range(position.lineNumber, 1, position.lineNumber, lineContent.length + 1),
                            text: hashes + cleanContent,
                            forceMoveMarkers: true
                        }]);
                    });
                }
            }

            if (htmlOutputElement) {
                htmlOutputInstance = monaco.editor.create(document.getElementById('html-output-container'), {
                    value: htmlOutputElement.value,
                    language: 'html',
                    theme: monacoTheme,
                    readOnly: true,
                    automaticLayout: true,
                    wordWrap: 'on'
                });
            }

            // --- Markdown Completion Provider ---
            monaco.languages.registerCompletionItemProvider('markdown', {
                provideCompletionItems: function(model, position) {
                    const word = model.getWordUntilPosition(position);
                    const range = {
                        startLineNumber: position.lineNumber,
                        endLineNumber: position.lineNumber,
                        startColumn: word.startColumn,
                        endColumn: word.endColumn
                    };
                    return {
                        suggestions: [{
                            label: 'table',
                            kind: monaco.languages.CompletionItemKind.Snippet,
                            insertText: [
                                '| ${1:Header1} | ${2:Header2} |',
                                '|---|---|',
                                '| ${3:Content1} | ${4:Content2} |',
                                ''
                            ].join('
'),
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            documentation: 'Table',
                            range: range
                        }]
                    };
                }
            });

            // --- View Mode Switcher ---
            const editorCol = document.getElementById('editor-column');
            const previewCol = document.getElementById('preview-column');
            const formRoot = document.getElementById('markdown-form');
            const radiosDesktop = document.querySelectorAll('input[name="view-mode-desktop"]');
            const radiosMobile = document.querySelectorAll('input[name="view-mode-mobile"]');

            function isMobile() {
                return window.innerWidth < 992;
            }

            function setViewMode(mode) {
                if (!editorCol || !previewCol || !formRoot) return;
                
                if (isMobile() && mode === 'split') {
                    mode = 'editor';
                }
                
                localStorage.setItem('markdown-view-mode', mode);

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
                        
                        const previewTabLink = document.querySelector('a[href="#preview"]');
                        if (previewTabLink) {
                            bootstrap.Tab.getOrCreateInstance(previewTabLink).show();
                        }
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

            radiosDesktop.forEach(r => r.addEventListener('change', (e) => setViewMode(e.target.value)));
            radiosMobile.forEach(r => r.addEventListener('change', (e) => setViewMode(e.target.value)));
            window.addEventListener('resize', applyInitialViewMode);
            applyInitialViewMode();

            if (window.$ && $.validator) $.validator.setDefaults({ ignore: [] });

            // --- Copy & Print Utils ---
            const copyButton = document.getElementById('copy-button');
            if (copyButton && htmlOutputInstance) {
                copyButton.addEventListener('click', function () {
                    const val = htmlOutputInstance.getValue();
                    if (!val) return;
                    navigator.clipboard.writeText(val).then(
                        () => { let h = copyButton.innerHTML; copyButton.innerHTML = `<i class="align-middle" data-lucide="check"></i> ${config.strings.copied}`; setTimeout(() => copyButton.innerHTML = h, 2000); },
                        (err) => console.error(err)
                    );
                });
            }
            const themeBtn = document.querySelector('.js-theme-toggle');
            if (themeBtn) themeBtn.addEventListener('click', () => setTimeout(() => document.getElementById('markdown-form').submit(), 1000));
            const printBtn = document.getElementById('print-button');
            if (printBtn) {
                printBtn.addEventListener('click', () => {
                    const isEditing = config.isEditing;
                    if (isEditing) {
                        const shareUrl = config.shareUrl;
                        window.open(shareUrl, '_blank');
                    } else {
                        window.print();
                    }
                });
            }

            // --- Table & Mermaid Rendering ---
            function stylePreviewTables() {
                const p = document.getElementById('preview');
                if(!p) return;
                p.querySelectorAll('table').forEach(t => {
                    t.classList.add('table', 'table-striped', 'table-bordered');
                    if (!t.parentElement.classList.contains('table-responsive')) {
                        const w = document.createElement('div'); w.className = 'table-responsive';
                        t.parentNode.insertBefore(w, t); w.appendChild(t);
                    }
                });
            }
            stylePreviewTables();
            if (window.mermaid) mermaid.initialize({ startOnLoad: false, theme: config.isDarkMode ? "dark" : "default" });
            function renderMermaid() {
                const p = document.getElementById('preview');
                if (!p || !window.mermaid) return;
                p.querySelectorAll('pre.mermaid, .mermaid').forEach((b, i) => {
                    const code = b.textContent || '';
                    const id = 'm-' + (i + 1) + '-' + Math.random().toString(36).slice(2);
                    try {
                        mermaid.parse(code);
                        mermaid.render(id, code).then(({ svg }) => {
                            const w = document.createElement('div'); w.innerHTML = svg; b.replaceWith(w);
                        }).catch(e => console.error(e));
                    } catch (e) { console.error(e); }
                });
            }
            const prevTab = document.querySelector('a[href="#preview"]');
            if (prevTab) prevTab.addEventListener('shown.bs.tab', renderMermaid);
            if (document.getElementById('preview')?.classList.contains('active')) renderMermaid();

            // --- MathJax Rendering ---
            function renderMath() {
                if (window.MathJax && window.MathJax.typesetPromise) {
                    const preview = document.getElementById('preview');
                    if(preview) window.MathJax.typesetPromise([preview]);
                }
            }
            if (prevTab) prevTab.addEventListener('shown.bs.tab', renderMath);
            if (document.getElementById('preview')?.classList.contains('active')) renderMath();

            // --- Highlight.js Rendering ---
            function renderHighlight() {
                if (window.hljs) {
                    hljs.highlightAll();
                }
            }
            if (prevTab) prevTab.addEventListener('shown.bs.tab', renderHighlight);
            if (document.getElementById('preview')?.classList.contains('active')) renderHighlight();

            // --- Public Link Copy ---
            const cpLink = document.getElementById('copy-link-button');
            const pubInp = document.getElementById('public-link');
            if (cpLink && pubInp) {
                cpLink.addEventListener('click', () => {
                    pubInp.select();
                    navigator.clipboard.writeText(pubInp.value).then(() => {
                        let h = cpLink.innerHTML; cpLink.innerHTML = `${config.strings.copied}`;
                        setTimeout(() => cpLink.innerHTML = h, 2000);
                    });
                });
            }
        });
    };
})();
