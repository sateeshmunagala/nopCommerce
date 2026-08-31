window.JobSupport = window.JobSupport || {};

JobSupport.render = function (button, data) {
    var scope = button.closest('.js-card, .js-detail__body, .job-support');
    var status = scope ? scope.querySelector('.js-status') : null;
    if (status) {
        status.textContent = data.message || button.getAttribute('data-js-error') || '';
        status.setAttribute('data-success', data.success ? 'true' : 'false');
    }
    if (data.success && button.hasAttribute('data-js-contact')) {
        var contact = scope.querySelector('.js-contact');
        if (contact) {
            contact.replaceChildren();
            var email = document.createElement('p');
            email.textContent = button.getAttribute('data-js-email') + ': ' + (data.email || '');
            var phone = document.createElement('p');
            phone.textContent = button.getAttribute('data-js-phone') + ': ' + (data.phone || '');
            contact.append(email, phone);
            contact.hidden = false;
        }
    }
};

JobSupport.post = function (button) {
    var url = button.getAttribute('data-js-url');
    var tokenField = document.querySelector('input[name="__RequestVerificationToken"]');
    if (!url || !tokenField) {
        JobSupport.render(button, { success: false, message: button.getAttribute('data-js-error') });
        return Promise.resolve();
    }
    button.setAttribute('aria-busy', 'true');
    button.disabled = true;
    return fetch(url, {
        method: 'POST',
        headers: { 'RequestVerificationToken': tokenField.value },
        credentials: 'same-origin'
    }).then(function (response) {
        return response.ok ? response.json() : { success: false, message: button.getAttribute('data-js-error') };
    }).then(function (data) {
        JobSupport.render(button, data);
    }).catch(function () {
        JobSupport.render(button, { success: false, message: button.getAttribute('data-js-error') });
    }).finally(function () {
        button.removeAttribute('aria-busy');
        button.disabled = false;
    });
};

document.querySelectorAll('.job-support .js-filter-details').forEach(function (details) {
    details.open = window.matchMedia('(min-width: 768px)').matches;
});

document.addEventListener('click', function (event) {
    var button = event.target.closest('[data-js-action]');
    if (!button)
        return;
    event.preventDefault();
    JobSupport.post(button);
});
