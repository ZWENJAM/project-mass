export function notificationPermission() {
    return typeof Notification === 'function' && 'serviceWorker' in navigator
        ? Notification.permission : 'unavailable';
}

export async function requestNotifications() {
    const permission = notificationPermission();
    return permission === 'default' ? await Notification.requestPermission() : permission;
}

export async function restFinished(nextExercise) {
    if (document.hidden && notificationPermission() === 'granted') {
        try {
            const registration = await navigator.serviceWorker.getRegistration();
            if (registration?.active && typeof registration.showNotification === 'function') {
                const title = nextExercise ? `Pause vorbei – nächster Satz: ${nextExercise}` : 'Pause vorbei';
                await registration.showNotification(title, { icon: 'icon-192.png', tag: 'project-mass-rest' });
                return;
            }
        } catch { /* The platform may block notifications even with permission. */ }
    }
    try { navigator.vibrate?.([200, 100, 200]); }
    catch { /* Unsupported vibration must not interrupt the workout. */ }
}
