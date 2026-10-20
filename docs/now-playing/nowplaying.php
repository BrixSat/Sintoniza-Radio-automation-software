<?php
/*
 * Now playing for the station's website: receives what Sintoniza Radio Broadcast system sends
 * (Settings → Website) and keeps it in three files next to this page:
 *
 *   nowplaying.txt   one line: "Artist - Title" (the station name when nothing is on air)
 *   nowplaying.json  everything that was sent: station, now and next (artist, title, type, duration, started)
 *   history.json     the last songs, newest first
 *
 * Opened in a browser (GET), the page answers with nowplaying.json, so the website's JavaScript can ask it.
 *
 * Setup: put this file in a folder of the website that PHP can write to, set the same key here and in the
 * app, and point the app at it (e.g. https://www.example.com/nowplaying/nowplaying.php).
 */

// The key set in the app (Settings → Website). Change it: anyone who knows the address could post otherwise.
const API_KEY = 'change-me';
// How many songs history.json keeps.
const HISTORY = 20;

$dir = __DIR__;

if ($_SERVER['REQUEST_METHOD'] === 'GET') {
    header('Content-Type: application/json; charset=utf-8');
    header('Access-Control-Allow-Origin: *');
    header('Cache-Control: no-cache');
    $file = "$dir/nowplaying.json";
    echo is_file($file) ? file_get_contents($file) : '{"now":null,"next":null}';
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    exit('Use POST');
}

if (!hash_equals(API_KEY, $_SERVER['HTTP_X_API_KEY'] ?? '')) {
    http_response_code(403);
    exit('Wrong key');
}

$data = json_decode(file_get_contents('php://input'), true);
if (!is_array($data) || !array_key_exists('now', $data)) {
    http_response_code(400);
    exit('Not now playing JSON');
}

/** Writes a file in one go (to a temporary file, then renamed), so the website never reads half of it. */
function save(string $file, string $text): void
{
    $tmp = $file . '.' . getmypid() . '.tmp';
    if (file_put_contents($tmp, $text) === false || !rename($tmp, $file)) {
        @unlink($tmp);
        http_response_code(500);
        exit("Can't write $file: check that PHP may write to this folder");
    }
}

/** "Artist - Title", or just the title. */
function line(?array $item): string
{
    if (!$item) return '';
    $artist = trim((string)($item['artist'] ?? ''));
    $title = trim((string)($item['title'] ?? ''));
    return $artist !== '' ? "$artist - $title" : $title;
}

$station = trim((string)($data['station'] ?? ''));
$now = is_array($data['now']) ? $data['now'] : null;
$json = JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_PRETTY_PRINT;

save("$dir/nowplaying.txt", ($now ? line($now) : $station) . "\n");
save("$dir/nowplaying.json", json_encode($data, $json));

// History: songs only, each once (the app also sends when only the next song changes).
if ($now && ($now['type'] ?? '') === 'music') {
    $history = json_decode(@file_get_contents("$dir/history.json") ?: '[]', true) ?: [];
    $last = $history[0] ?? null;
    if (!$last || line($last) !== line($now) || ($last['started'] ?? '') !== ($now['started'] ?? '')) {
        array_unshift($history, $now);
        save("$dir/history.json", json_encode(array_slice($history, 0, HISTORY), $json));
    }
}

echo 'OK';
