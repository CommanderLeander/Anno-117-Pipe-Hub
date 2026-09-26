const fs = require('fs');
const source = fs.readFileSync('src/AnnoPipeHub/wwwroot/app.js', 'utf8');

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

assert(source.includes("'pipe.timeout': 'Verbindungsversuch abgelaufen'"), 'German timeout translation missing.');
assert(source.includes("'pipe.timeout': 'Connection attempt timed out'"), 'English timeout translation missing.');
assert(source.includes("'reconnect.scheduled': 'Erneuter Versuch in {seconds} s'"), 'German reconnect translation missing.');
assert(source.includes("'reconnect.scheduled': 'Retrying in {seconds} s'"), 'English reconnect translation missing.');
assert(source.includes("'pipe.notFound': 'Pipe nicht gefunden / Anno möglicherweise noch nicht gestartet'"), 'German not-found translation missing.');
assert(source.includes("'pipe.notFound': 'Pipe not found / Anno may not be started yet'"), 'English not-found translation missing.');
assert(source.includes('eventTranslations[language]'), 'Debug renderer does not translate from event keys.');
assert(source.includes('renderDebug()'), 'Debug entries are not rerendered on language changes.');
console.log('PASS Debug-Übersetzungen Deutsch/Englisch');
