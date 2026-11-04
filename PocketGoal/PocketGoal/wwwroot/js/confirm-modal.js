/**
 * PocketGoal Reusable Accessible Modal (Confirm & Alert)
 * Exposes window.PG.confirm and window.PG.alert promise-based APIs.
 */
(function () {
    window.PG = window.PG || {};

    let modalElement = null;
    let bsModal = null;
    let titleEl = null;
    let messageEl = null;
    let confirmBtn = null;
    let cancelBtn = null;
    let dangerIcon = null;
    let infoIcon = null;
    let successIcon = null;
    let lastActiveElement = null;
    let currentResolver = null;

    function initElements() {
        if (modalElement && bsModal) return true;
        modalElement = document.getElementById('pgCommonModal');
        if (!modalElement || !window.bootstrap) return false;

        bsModal = bootstrap.Modal.getOrCreateInstance(modalElement, {
            backdrop: 'static',
            keyboard: true
        });

        titleEl = document.getElementById('pgCommonModalTitle');
        messageEl = document.getElementById('pgCommonModalMessage');
        confirmBtn = document.getElementById('pgCommonModalConfirmBtn');
        cancelBtn = document.getElementById('pgCommonModalCancelBtn');
        dangerIcon = document.getElementById('pgCommonModalDangerIcon');
        infoIcon = document.getElementById('pgCommonModalInfoIcon');
        successIcon = document.getElementById('pgCommonModalSuccessIcon');

        confirmBtn?.addEventListener('click', function () {
            if (currentResolver) {
                const resolve = currentResolver;
                currentResolver = null;
                bsModal.hide();
                resolve(true);
            }
        });

        cancelBtn?.addEventListener('click', function () {
            if (currentResolver) {
                const resolve = currentResolver;
                currentResolver = null;
                bsModal.hide();
                resolve(false);
            }
        });

        modalElement.addEventListener('hidden.bs.modal', function () {
            if (currentResolver) {
                const resolve = currentResolver;
                currentResolver = null;
                resolve(false);
            }
            if (lastActiveElement && typeof lastActiveElement.focus === 'function') {
                lastActiveElement.focus();
            }
        });

        modalElement.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                confirmBtn?.click();
            }
        });

        return true;
    }

    /**
     * Reusable confirm modal returning Promise<boolean>
     * @param {Object} options
     * @param {string} options.title
     * @param {string} options.message
     * @param {string} [options.confirmText='OK']
     * @param {string} [options.cancelText='Cancel']
     * @param {'primary'|'danger'|'success'} [options.variant='primary']
     * @returns {Promise<boolean>}
     */
    PG.confirm = function (options) {
        options = options || {};
        const title = options.title || 'Confirm Action';
        const message = options.message || 'Are you sure you want to proceed?';
        const confirmText = options.confirmText || 'OK';
        const cancelText = options.cancelText || 'Cancel';
        const variant = options.variant || 'primary';

        if (!initElements()) {
            return Promise.resolve(window.confirm(message));
        }

        lastActiveElement = document.activeElement;

        // Safe text encoding via textContent
        if (titleEl) titleEl.textContent = title;
        if (messageEl) messageEl.textContent = message;

        if (cancelBtn) {
            cancelBtn.classList.remove('d-none');
            cancelBtn.innerHTML = `<i class="bi bi-x-lg me-1"></i> ${escapeText(cancelText)}`;
        }

        if (confirmBtn) {
            confirmBtn.className = 'btn rounded-pill px-4 flex-grow-1 flex-sm-grow-0';
            if (variant === 'danger') {
                confirmBtn.classList.add('btn-danger');
                confirmBtn.innerHTML = `<i class="bi bi-trash me-1"></i> ${escapeText(confirmText)}`;
            } else if (variant === 'success') {
                confirmBtn.classList.add('btn-success');
                confirmBtn.innerHTML = `<i class="bi bi-check-lg me-1"></i> ${escapeText(confirmText)}`;
            } else {
                confirmBtn.classList.add('btn-primary');
                confirmBtn.innerHTML = `<i class="bi bi-check-lg me-1"></i> ${escapeText(confirmText)}`;
            }
        }

        dangerIcon?.classList.toggle('d-none', variant !== 'danger');
        successIcon?.classList.toggle('d-none', variant !== 'success');
        infoIcon?.classList.toggle('d-none', variant === 'danger' || variant === 'success');

        return new Promise(function (resolve) {
            currentResolver = resolve;
            bsModal.show();
            setTimeout(function () {
                if (variant === 'danger' && cancelBtn) {
                    cancelBtn.focus();
                } else if (confirmBtn) {
                    confirmBtn.focus();
                }
            }, 100);
        });
    };

    /**
     * Reusable alert modal returning Promise<void>
     * @param {Object} options
     * @param {string} options.title
     * @param {string} options.message
     * @param {string} [options.okText='OK']
     * @param {'primary'|'danger'|'success'} [options.variant='primary']
     * @returns {Promise<void>}
     */
    PG.alert = function (options) {
        options = options || {};
        const title = options.title || 'Notice';
        const message = options.message || '';
        const okText = options.okText || 'OK';
        const variant = options.variant || 'primary';

        if (!initElements()) {
            window.alert(message);
            return Promise.resolve();
        }

        lastActiveElement = document.activeElement;

        if (titleEl) titleEl.textContent = title;
        if (messageEl) messageEl.textContent = message;

        if (cancelBtn) cancelBtn.classList.add('d-none');

        if (confirmBtn) {
            confirmBtn.className = 'btn rounded-pill px-4 flex-grow-1 flex-sm-grow-0';
            if (variant === 'danger') {
                confirmBtn.classList.add('btn-danger');
            } else if (variant === 'success') {
                confirmBtn.classList.add('btn-success');
            } else {
                confirmBtn.classList.add('btn-primary');
            }
            confirmBtn.innerHTML = `<i class="bi bi-check-lg me-1"></i> ${escapeText(okText)}`;
        }

        dangerIcon?.classList.toggle('d-none', variant !== 'danger');
        successIcon?.classList.toggle('d-none', variant !== 'success');
        infoIcon?.classList.toggle('d-none', variant === 'danger' || variant === 'success');

        return new Promise(function (resolve) {
            currentResolver = function () { resolve(); };
            bsModal.show();
            setTimeout(function () {
                confirmBtn?.focus();
            }, 100);
        });
    };

    function escapeText(text) {
        const div = document.createElement('div');
        div.textContent = text || '';
        return div.innerHTML;
    }

    // Intercept data-confirm and data-bs-confirm attributes for forms & action buttons
    document.addEventListener('click', async function (e) {
        const trigger = e.target.closest('[data-confirm], [data-bs-confirm="true"]');
        if (!trigger) return;

        e.preventDefault();
        e.stopPropagation();

        const form = trigger.closest('form');
        const message = trigger.getAttribute('data-confirm') ||
                        trigger.getAttribute('data-confirm-message') ||
                        'Are you sure you want to proceed?';
        const title = trigger.getAttribute('data-confirm-title') || 'Confirm Action';
        const confirmText = trigger.getAttribute('data-confirm-btn-text') || 'Delete';
        const cancelText = trigger.getAttribute('data-confirm-cancel-text') || 'Cancel';
        const variant = trigger.getAttribute('data-confirm-variant') || 'danger';

        const confirmed = await PG.confirm({
            title: title,
            message: message,
            confirmText: confirmText,
            cancelText: cancelText,
            variant: variant
        });

        if (confirmed && form) {
            form.submit();
        }
    }, true);
})();
