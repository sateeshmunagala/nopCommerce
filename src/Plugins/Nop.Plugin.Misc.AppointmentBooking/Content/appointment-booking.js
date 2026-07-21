(function () {
    window.nopAppointmentBooking = window.nopAppointmentBooking || {};

    function setActiveDate(root, dateKey) {
        root.querySelectorAll('[data-appointment-date]').forEach(function (button) {
            var isActive = button.getAttribute('data-appointment-date') === dateKey;
            button.classList.toggle('active', isActive);
            button.setAttribute('aria-selected', isActive ? 'true' : 'false');
        });

        root.querySelectorAll('[data-appointment-time-group]').forEach(function (group) {
            var isActive = group.getAttribute('data-appointment-time-group') === dateKey;
            group.classList.toggle('active', isActive);

            if (!isActive) {
                group.querySelectorAll('input[type="radio"]').forEach(function (radio) {
                    radio.checked = false;
                });
            }
        });
    }

    document.addEventListener('click', function (event) {
        var dateButton = event.target.closest('[data-appointment-date]');
        if (!dateButton) {
            return;
        }

        var root = dateButton.closest('.appointment-booking');
        if (!root) {
            return;
        }

        setActiveDate(root, dateButton.getAttribute('data-appointment-date'));
    });

    document.addEventListener('submit', function (event) {
        var form = event.target.closest('[data-appointment-booking-form]');
        if (!form) {
            return;
        }

        var selectedTime = form.querySelector('input[name="startUtc"]:checked');
        var validationMessage = form.querySelector('[data-appointment-validation-message]');

        if (selectedTime) {
            if (validationMessage) {
                validationMessage.hidden = true;
            }
            return;
        }

        event.preventDefault();
        if (validationMessage) {
            validationMessage.hidden = false;
            if (typeof validationMessage.focus === 'function') {
                validationMessage.focus();
            }
        }
    });

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-appointment-timezone]').forEach(function (element) {
            try {
                element.textContent = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Local time';
            } catch (error) {
                element.textContent = 'Local time';
            }
        });
    });
})();
