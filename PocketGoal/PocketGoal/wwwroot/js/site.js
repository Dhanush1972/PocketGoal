/**
 * PocketGoal - Modern UX Interactions & Accessibility Helpers
 */

document.addEventListener('DOMContentLoaded', function () {
    // 1. Auto-dismiss Alert Banners with progress countdown
    const alertBanners = document.querySelectorAll('.pg-alert-banner, .alert-dismissible');
    alertBanners.forEach(function (alert) {
        // Add progress bar if not present
        if (!alert.querySelector('.pg-alert-progress')) {
            const progressBar = document.createElement('div');
            progressBar.className = 'pg-alert-progress';
            alert.appendChild(progressBar);
        }

        let timeoutId = setTimeout(function () {
            const bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
            if (bsAlert) bsAlert.close();
        }, 5000);

        // Pause on mouseenter, resume on mouseleave
        alert.addEventListener('mouseenter', function () {
            clearTimeout(timeoutId);
            const progress = alert.querySelector('.pg-alert-progress');
            if (progress) progress.style.animationPlayState = 'paused';
        });

        alert.addEventListener('mouseleave', function () {
            const progress = alert.querySelector('.pg-alert-progress');
            if (progress) progress.style.animationPlayState = 'running';
            timeoutId = setTimeout(function () {
                const bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
                if (bsAlert) bsAlert.close();
            }, 3000);
        });
    });

    // 2. Prevent Double Submit & Show Loading Spinner on Form Submission
    document.querySelectorAll('form').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            // Check if jQuery validation is present and invalid
            if (window.jQuery && $(form).data('validator')) {
                if (!$(form).valid()) {
                    return; // Validation failed, do not show spinner
                }
            }

            const submitBtn = form.querySelector('button[type="submit"]:not(.no-spin)');
            if (submitBtn && !submitBtn.classList.contains('is-submitting')) {
                submitBtn.classList.add('is-submitting');
                submitBtn.setAttribute('aria-busy', 'true');
                
                const originalHtml = submitBtn.innerHTML;
                submitBtn.setAttribute('data-original-html', originalHtml);

                const loadingText = submitBtn.getAttribute('data-loading-text') || 'Saving...';
                submitBtn.innerHTML = `<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span><span>${loadingText}</span>`;
            }
        });
    });

    // 3. Quick-Amount & Quick-Date Preset Chips
    document.querySelectorAll('.quick-chip').forEach(function (chip) {
        chip.addEventListener('click', function () {
            const targetInputId = chip.getAttribute('data-target-input');
            const targetInput = targetInputId ? document.getElementById(targetInputId) : null;
            const amountVal = chip.getAttribute('data-amount');
            const mode = chip.getAttribute('data-mode') || 'set'; // 'set' or 'add'

            if (targetInput) {
                let currentVal = parseFloat(targetInput.value) || 0;
                let newVal = parseFloat(amountVal);

                if (mode === 'add') {
                    newVal = currentVal + newVal;
                }

                targetInput.value = newVal;
                // Dispatch input/change event for reactive preview scripts
                targetInput.dispatchEvent(new Event('input', { bubbles: true }));
                targetInput.dispatchEvent(new Event('change', { bubbles: true }));
                targetInput.focus();
            }

            // Optional: Toggle active visual state within group
            const parent = chip.closest('.quick-chip-group');
            if (parent) {
                parent.querySelectorAll('.quick-chip').forEach(c => c.classList.remove('active'));
                chip.classList.add('active');
            }
        });
    });

    // 4. Accessible Reusable Confirmation Modal for Destructive Actions
    const confirmModalEl = document.getElementById('pgConfirmActionModal');
    let confirmModal = null;
    let pendingFormToSubmit = null;

    if (confirmModalEl && window.bootstrap) {
        confirmModal = new bootstrap.Modal(confirmModalEl);
        const confirmBtn = document.getElementById('pgConfirmActionModalBtn');
        const titleEl = document.getElementById('pgConfirmActionModalTitle');
        const messageEl = document.getElementById('pgConfirmActionModalMessage');

        if (confirmBtn) {
            confirmBtn.addEventListener('click', function () {
                if (pendingFormToSubmit) {
                    confirmBtn.disabled = true;
                    confirmBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>Deleting...';
                    pendingFormToSubmit.submit();
                }
            });
        }

        // Attach listener to any element triggering confirm modal
        document.addEventListener('click', function (e) {
            const trigger = e.target.closest('[data-bs-confirm="true"]');
            if (trigger) {
                e.preventDefault();
                pendingFormToSubmit = trigger.closest('form');
                const title = trigger.getAttribute('data-confirm-title') || 'Confirm Delete';
                const message = trigger.getAttribute('data-confirm-message') || 'Are you sure you want to proceed? This action cannot be undone.';
                const actionLabel = trigger.getAttribute('data-confirm-btn-text') || 'Yes, Delete';

                if (titleEl) titleEl.textContent = title;
                if (messageEl) messageEl.innerHTML = message;
                if (confirmBtn) {
                    confirmBtn.disabled = false;
                    confirmBtn.innerHTML = `<i class="bi bi-trash me-1"></i> ${actionLabel}`;
                }

                confirmModal.show();
            }
        });
    }

    // 5. Category Color Swatches Helper
    const colorInput = document.getElementById('categoryColorInput');
    const colorSwatches = document.querySelectorAll('.color-swatch-btn');
    if (colorInput && colorSwatches.length > 0) {
        colorSwatches.forEach(function (swatch) {
            swatch.addEventListener('click', function () {
                const color = swatch.getAttribute('data-color');
                colorInput.value = color;
                colorSwatches.forEach(s => s.classList.remove('active'));
                swatch.classList.add('active');
            });
        });
        
        colorInput.addEventListener('input', function () {
            colorSwatches.forEach(function (swatch) {
                if (swatch.getAttribute('data-color').toLowerCase() === colorInput.value.toLowerCase()) {
                    swatch.classList.add('active');
                } else {
                    swatch.classList.remove('active');
                }
            });
        });
    }
});
