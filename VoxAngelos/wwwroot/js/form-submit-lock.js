(function () {
    'use strict';

    const style = document.createElement('style');
    style.textContent = `
        form.va-form-submitting {
            cursor: wait;
            opacity: 0.78;
            pointer-events: none;
        }
        form.va-form-submitting [type="submit"] {
            cursor: wait !important;
        }
    `;
    document.head.appendChild(style);

    document.addEventListener('submit', event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || form.dataset.allowMultipleSubmit === 'true') {
            return;
        }

        if (form.dataset.submitting === 'true') {
            event.preventDefault();
            return;
        }

        // Form-level validation and custom handlers run before this document-level
        // listener. Do not lock a form when one of them rejected the submission.
        if (event.defaultPrevented) {
            return;
        }

        form.dataset.submitting = 'true';
        form.classList.add('va-form-submitting');
        form.setAttribute('aria-busy', 'true');
        form.setAttribute('inert', '');

        const submitter = event.submitter;
        if (submitter instanceof HTMLElement) {
            submitter.setAttribute('aria-disabled', 'true');
            if (submitter instanceof HTMLButtonElement && submitter.dataset.keepSubmitText !== 'true') {
                submitter.dataset.originalText = submitter.textContent || '';
                submitter.textContent = submitter.dataset.submittingText || 'Please wait…';
            }
        }
    });
})();
