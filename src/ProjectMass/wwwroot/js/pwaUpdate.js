if ('serviceWorker' in navigator) {
    const banner = document.querySelector('#app-update');
    const reload = banner.querySelector('button');
    const workers = navigator.serviceWorker;
    let candidate = null;
    let reloading = false;
    let controller = workers.controller;

    workers.addEventListener('controllerchange', () => {
        if (reloading) {
            window.location.reload();
        } else if (controller && controller !== workers.controller) {
            candidate = null;
            banner.hidden = false;
        }
        controller = workers.controller;
    });

    reload.addEventListener('click', () => {
        if (candidate?.state === 'installed') {
            reloading = true;
            reload.disabled = true;
            candidate.postMessage({ type: 'SKIP_WAITING' });
        } else {
            window.location.reload();
        }
    });

    workers.register('service-worker.js', { updateViaCache: 'none' }).then(registration => {
        const show = () => {
            if (!registration.waiting || !workers.controller) return;
            candidate = registration.waiting;
            banner.hidden = false;
        };
        const watchInstalling = () => registration.installing?.addEventListener('statechange', show);
        registration.addEventListener('updatefound', watchInstalling);
        watchInstalling();
        show();
    }).catch(error => console.warn('Die Offline-Funktion konnte nicht eingerichtet werden.', error));
}
