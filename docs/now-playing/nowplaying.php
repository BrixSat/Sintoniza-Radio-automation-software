<?php
/*
 * Now playing for the station's website: receives what Sintoniza Radio Broadcast system sends
 * (Settings → Website) and keeps it in three files next to this page:
 *
 *   nowplaying.txt   one line: "Artist - Title" (the station name when nothing is on air)
 *   nowplaying.json  everything that was sent: station, now and next (artist, title, type, duration, started),
 *                    the live show, and "cover": the address of cover.jpg / cover.png when the app sends covers
 *   history.json     the last songs, newest first
 *
 * Opened in a browser (GET), the page answers with nowplaying.json, so the website's JavaScript can ask it.
 * It also keeps the files the app uploads with PUT (Settings → Website → Files, nowplaying.php?file=cover.jpg).
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

if (!in_array($_SERVER['REQUEST_METHOD'], ['POST', 'PUT'], true)) {
    http_response_code(405);
    exit('Use POST');
}

if (!hash_equals(API_KEY, $_SERVER['HTTP_X_API_KEY'] ?? '')) {
    http_response_code(403);
    exit('Wrong key');
}

// A file from Settings → Website → Files (an address ending in ?file=name): saved next to this page as it comes.
// Only plain names of text, page and picture files, so nothing that runs (.php) can be put on the website.
if ($_SERVER['REQUEST_METHOD'] === 'PUT') {
    $name = (string)($_GET['file'] ?? '');
    if (!preg_match('/^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.(txt|xml|html|htm|json|jpg|jpeg|png)$/i', $name)
        || in_array(strtolower($name), ['nowplaying.json', 'history.json'], true)) {
        http_response_code(400);
        exit('Use ?file= with a name such as nowplaying.txt, nowplaying.xml or cover.jpg');
    }
    $body = file_get_contents('php://input');
    if ($body === false || strlen($body) > 2 * 1024 * 1024) {
        http_response_code(413);
        exit('Too big');
    }
    save("$dir/$name", $body);
    exit('OK');
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

// The cover picture (Send the song's cover picture too): kept as cover.jpg or cover.png, and nowplaying.json gets
// its address instead (with ?the time, so browsers fetch the new one).
if (is_string($data['cover'] ?? null) && preg_match('#^data:image/(jpeg|png);base64,(.+)$#s', $data['cover'], $m)) {
    $ext = $m[1] === 'png' ? 'png' : 'jpg';
    $picture = base64_decode($m[2], true);
    if ($picture !== false) {
        save("$dir/cover.$ext", $picture);
        $data['cover'] = "cover.$ext?" . time();
    } else {
        unset($data['cover']);
    }
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
