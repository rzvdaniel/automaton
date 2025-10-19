// Monaco Editor Integration for Automaton Apps
window.MonacoEditorInterop = {
    editor: null,
    
    async initialize(elementId, initialValue, language) {
        const element = document.getElementById(elementId);
        if (!element) {
            console.error(`Element with id ${elementId} not found`);
            return false;
        }

        // Wait for Monaco to be loaded
        if (typeof monaco === 'undefined') {
            console.error('Monaco editor is not loaded');
            return false;
        }

        try {
            this.editor = monaco.editor.create(element, {
                value: initialValue || '',
                language: language || 'html',
                theme: 'vs-dark',
                automaticLayout: true,
                minimap: { enabled: true },
                fontSize: 14,
                lineNumbers: 'on',
                roundedSelection: false,
                scrollBeyondLastLine: false,
                readOnly: false,
                wordWrap: 'on'
            });

            return true;
        } catch (error) {
            console.error('Error initializing Monaco editor:', error);
            return false;
        }
    },

    getValue() {
        if (this.editor) {
            return this.editor.getValue();
        }
        return '';
    },

    setValue(value) {
        if (this.editor) {
            this.editor.setValue(value || '');
        }
    },

    setLanguage(language) {
        if (this.editor) {
            const model = this.editor.getModel();
            if (model) {
                monaco.editor.setModelLanguage(model, language);
            }
        }
    },

    dispose() {
        if (this.editor) {
            this.editor.dispose();
            this.editor = null;
        }
    },

    focus() {
        if (this.editor) {
            this.editor.focus();
        }
    },

    layout() {
        if (this.editor) {
            this.editor.layout();
        }
    }
};
