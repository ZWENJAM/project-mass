const databaseName = 'project-mass';
const databaseVersion = 3;
// Aggregate roots and initialization metadata, matching Services/StorageStores.cs.
const stores = ['userProfiles', 'exercises', 'workoutPlans', 'workoutSessions',
    'appMetadata', 'searchCache', 'syncMetadata',
    'foodItems', 'foodLogEntries', 'recipes', 'bodyMeasurements', 'waterLogs', 'notes'];
let databasePromise;
const changeId = '00000000-0000-0000-0000-000000000009';

function openDatabase() {
    if (!databasePromise) {
        databasePromise = new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, databaseVersion);
            request.onupgradeneeded = event => {
                for (const store of stores) {
                    if (!request.result.objectStoreNames.contains(store)) {
                        request.result.createObjectStore(store, { keyPath: 'id' });
                    }
                }
                // Earlier versions could contain edits made before change tracking existed.
                if (event.oldVersion > 0 && event.oldVersion < 3) {
                    request.transaction.objectStore('syncMetadata').put({ id: changeId, at: new Date().toISOString() });
                }
            };
            request.onsuccess = () => {
                const db = request.result;
                db.onversionchange = () => { db.close(); databasePromise = undefined; };
                resolve(db);
            };
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error('Datenbank-Update blockiert. Bitte andere PROJECT MASS-Tabs schließen.'));
        }).catch(error => { databasePromise = undefined; throw error; });
    }
    return databasePromise;
}

async function execute(store, mode, action) {
    if (!stores.includes(store)) throw new Error(`Unbekannter Object Store: ${store}`);
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction(store, mode);
        const request = action(transaction.objectStore(store));
        // Resolve writes only once the transaction has committed, not on request success.
        transaction.oncomplete = () => resolve(request.result ?? null);
        transaction.onerror = () => reject(transaction.error ?? request.error);
        transaction.onabort = () => reject(transaction.error ?? new Error('Datenbanktransaktion abgebrochen.'));
    });
}

export function get(store, id) { return execute(store, 'readonly', objectStore => objectStore.get(id)); }
export function getAll(store) { return execute(store, 'readonly', objectStore => objectStore.getAll()); }
export function put(store, item) {
    if (!item || typeof item.id !== 'string' || !item.id) throw new Error('Eintrag benötigt eine ID.');
    return execute(store, 'readwrite', objectStore => objectStore.put(item));
}
function deleteEntry(store, id) { return execute(store, 'readwrite', objectStore => objectStore.delete(id)); }
export { deleteEntry as delete };
export function clear(store) { return execute(store, 'readwrite', objectStore => objectStore.clear()); }

export async function isEmpty(names) {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(names, 'readonly');
        const counts = names.map(name => tx.objectStore(name).count());
        tx.oncomplete = () => resolve(counts.every(request => request.result === 0));
        tx.onabort = () => reject(tx.error ?? new Error('Datenstand konnte nicht geprüft werden.'));
    });
}

export async function seedIfEmpty(names, seeds) {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(names, 'readwrite');
        const counts = names.map(name => tx.objectStore(name).count());
        counts.at(-1).onsuccess = () => {
            if (counts.some(request => request.result !== 0)) return;
            for (const [name, items] of Object.entries(seeds))
                for (const item of items) tx.objectStore(name).add(item);
        };
        tx.oncomplete = () => resolve();
        tx.onabort = () => reject(tx.error ?? new Error('Startdaten konnten nicht angelegt werden.'));
    });
}

export async function changeTracked(store, operation, item, id, at) {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction([store, 'syncMetadata'], 'readwrite');
        const items = tx.objectStore(store);
        const meta = tx.objectStore('syncMetadata');
        tx.oncomplete = () => resolve();
        tx.onabort = () => reject(tx.error ?? new Error('Änderung konnte nicht gespeichert werden.'));
        const previous = meta.get(changeId);
        previous.onsuccess = () => {
            const timestamp = Math.max(Date.parse(at), (Date.parse(previous.result?.at) || 0) + 1);
            if (operation === 'put') items.put(item);
            if (operation === 'delete') items.delete(id);
            if (operation === 'clear') items.clear();
            meta.put({ id: changeId, at: new Date(timestamp).toISOString() });
        };
    });
}

export async function replaceForSync(backup, expectedChange) {
    const replacement = {
        userProfiles: backup.profile ? [backup.profile] : [],
        exercises: backup.exercises, workoutPlans: backup.workoutPlans, workoutSessions: backup.workoutSessions,
        foodItems: backup.foodItems, foodLogEntries: backup.foodLogEntries, recipes: backup.recipes,
        bodyMeasurements: backup.bodyMeasurements, waterLogs: backup.waterLogs, notes: backup.notes,
        appMetadata: backup.appMetadata
    };
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction([...Object.keys(replacement), 'syncMetadata'], 'readwrite');
        let applied = false;
        tx.oncomplete = () => resolve(applied);
        tx.onabort = () => reject(tx.error ?? new Error('NAS-Stand konnte nicht übernommen werden.'));
        const marker = tx.objectStore('syncMetadata').get(changeId);
        marker.onsuccess = () => {
            if ((marker.result ? Date.parse(marker.result.at) : null) !== (expectedChange ? Date.parse(expectedChange) : null)) return;
            const sessions = tx.objectStore('workoutSessions').openCursor();
            sessions.onsuccess = () => {
                const cursor = sessions.result;
                if (cursor) {
                    if (cursor.value.isCompleted) cursor.continue();
                    return;
                }
                for (const [name, items] of Object.entries(replacement)) {
                    const store = tx.objectStore(name);
                    store.clear();
                    for (const item of items) store.put(item);
                }
                applied = true;
            };
        };
    });
}

export async function markSyncBackup(id, at) {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const tx = db.transaction('userProfiles', 'readwrite');
        const profiles = tx.objectStore('userProfiles');
        const request = profiles.get(id);
        request.onsuccess = () => profiles.put({ ...(request.result ?? { id }), lastBackupAt: at });
        tx.oncomplete = () => resolve();
        tx.onabort = () => reject(tx.error ?? new Error('Backup-Zeitpunkt konnte nicht gespeichert werden.'));
    });
}
