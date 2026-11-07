/**
 * RoadReady email relay (delivery option B) - a free Google Apps Script web app.
 *
 * What it does, in plain English:
 *   The RoadReady app sends it a small JSON message after a learner finishes a session. This script checks that the
 *   message is allowed, builds the report email itself, and sends it with Gmail to the driving school.
 *
 * Safety checks (all of them must pass, otherwise nothing is sent):
 *   1. The message carries the shared SECRET.
 *   2. The recipient is on the ALLOWED_RECIPIENTS list (the school's login email).
 *   3. The session id looks right and has not been sent before (so a retry never sends a second email).
 *   4. Every value that a person typed (names, scenario names) is cleaned and HTML-escaped before it goes in the email.
 *      The app sends plain fields, not HTML, so nobody can make this script send their own HTML.
 *
 * Limits to know about:
 *   - The SECRET is stored inside the RoadReady build, so someone determined could extract it. That is why the
 *     recipient allowlist exists: the worst case is a fake report to a school that is already on your list.
 *   - A normal Gmail account can send about 100 emails a day (MailApp.getRemainingDailyQuota()).
 *
 * Setup: see the README, section "End-of-session email report".
 */

// ============ EDIT THESE THREE THINGS BEFORE YOU DEPLOY ============
var SHARED_SECRET = 'PASTE-A-LONG-RANDOM-STRING-HERE';          // at least 24 random characters; same value goes in ReportSettings
var ALLOWED_RECIPIENTS = ['school@example.com'];                // the login email of every driving school that may get reports
var SENDER_NAME = 'RoadReady';                                  // the "from" name people see
// ====================================================================

var MAX_ATTEMPTS = 100;
var TEXT_LIMIT = 80;
var KEEP_SENT_IDS_DAYS = 60;

/** Opening the web app URL in a browser shows this line, so you can check the deployment works. */
function doGet() {
  return json_({ ok: true, message: 'RoadReady email relay is running.' });
}

/** The app calls this. */
function doPost(e) {
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(10000); // one message at a time, so two taps cannot both pass the "already sent" check

    var data = JSON.parse(e.postData.contents);

    if (SHARED_SECRET.indexOf('PASTE-') === 0 || !safeEquals_(String(data.secret || ''), SHARED_SECRET)) {
      return json_({ ok: false, error: 'unauthorized' });
    }

    var to = String(data.to || '').trim().toLowerCase();
    if (!isAllowed_(to)) {
      return json_({ ok: false, error: 'recipient not allowed' });
    }

    var sessionId = String(data.sessionId || '');
    if (!/^[A-Za-z0-9\-]{8,64}$/.test(sessionId)) {
      return json_({ ok: false, error: 'bad session id' });
    }

    var props = PropertiesService.getScriptProperties();
    if (props.getProperty('sent_' + sessionId)) {
      return json_({ ok: true, duplicate: true }); // already sent: report success so the app stops retrying
    }

    if (MailApp.getRemainingDailyQuota() < 1) {
      return json_({ ok: false, error: 'daily email limit reached' });
    }

    var email = buildEmail_(data);
    if (!sendEmail_(to, email, props)) {
      return json_({ ok: false, error: 'email provider refused the message' });
    }

    props.setProperty('sent_' + sessionId, String(new Date().getTime()));
    pruneOldIds_(props);
    return json_({ ok: true });
  } catch (err) {
    return json_({ ok: false, error: 'server error' });
  } finally {
    try { lock.releaseLock(); } catch (ignore) {}
  }
}

// ------------------------------------------------------------ building the email

function buildEmail_(data) {
  var school = clean_(data.schoolName);
  var learner = clean_(data.learnerName);
  var total = clampInt_(data.totalMarks, 0, 100);
  var duration = clampInt_(data.durationSeconds, 0, 86400);
  var when = formatDate_(data.endUtc);
  var explanation = 'Overall score: average of best attempt per scenario';
  var footer = 'Simulated training result sent by RoadReady.';

  var rows = [];
  var attempts = Array.isArray(data.attempts) ? data.attempts.slice(0, MAX_ATTEMPTS) : [];
  for (var i = 0; i < attempts.length; i++) {
    rows.push({ label: clean_(attempts[i].label), score: clampInt_(attempts[i].score, 0, 100) });
  }

  var subject = 'RoadReady session report: ' + learner + ' - ' + total;

  var text = 'RoadReady session report\n\n' +
    'School: ' + school + '\n' +
    'Learner: ' + learner + '\n' +
    'Date: ' + when + '\n' +
    'Session length: ' + durationText_(duration) + '\n' +
    'Total marks: ' + total + ' / 100\n' +
    explanation + '\n\nAttempts:\n';
  for (var t = 0; t < rows.length; t++) {
    text += '- ' + rows[t].label + ': ' + rows[t].score + ' / 100\n';
  }
  text += '\n' + footer + '\n';

  // The grade bands match the game's scoring (85 / 70 / 50).
  var grade = gradeFor_(total);

  // Email programs ignore most modern CSS, so the layout uses tables and inline styles.
  var html =
    '<div style="background:#f3f5f4;padding:24px 12px;font-family:Arial,Helvetica,sans-serif;color:#1a1a1a">' +
    '<table role="presentation" align="center" width="100%" style="max-width:600px;border-collapse:collapse;background:#ffffff;border-radius:12px;overflow:hidden">' +
    // Header band with the company name
    '<tr><td style="background:#1b5e3b;padding:22px 28px">' +
      '<div style="font-size:26px;font-weight:bold;color:#ffffff;letter-spacing:0.5px">Road<span style="color:#fad201">Ready</span></div>' +
      '<div style="font-size:13px;color:#cfe6d9;margin-top:4px">Driving session report</div>' +
    '</td></tr>' +
    '<tr><td style="height:5px;background:#fad201;font-size:0;line-height:0">&nbsp;</td></tr>' +
    // Who and when
    '<tr><td style="padding:24px 28px 8px">' +
      '<div style="font-size:13px;color:#6b7280">Learner</div>' +
      '<div style="font-size:22px;font-weight:bold;margin:2px 0 14px">' + esc_(learner) + '</div>' +
      '<table role="presentation" width="100%" style="border-collapse:collapse;font-size:14px">' +
        '<tr><td style="color:#6b7280;padding:3px 0;width:130px">Driving school</td><td style="padding:3px 0">' + esc_(school) + '</td></tr>' +
        '<tr><td style="color:#6b7280;padding:3px 0">Date</td><td style="padding:3px 0">' + esc_(when) + '</td></tr>' +
        '<tr><td style="color:#6b7280;padding:3px 0">Session length</td><td style="padding:3px 0">' + esc_(durationText_(duration)) + '</td></tr>' +
        '<tr><td style="color:#6b7280;padding:3px 0">Attempts</td><td style="padding:3px 0">' + rows.length + '</td></tr>' +
      '</table>' +
    '</td></tr>' +
    // The big result
    '<tr><td style="padding:12px 28px">' +
      '<table role="presentation" width="100%" style="border-collapse:collapse;background:' + grade.soft + ';border-radius:10px">' +
        '<tr><td style="padding:18px 22px">' +
          '<div style="font-size:13px;color:#4b5563">Total marks</div>' +
          '<div style="font-size:44px;font-weight:bold;line-height:1.1;color:' + grade.color + '">' + total + '<span style="font-size:20px;color:#6b7280"> / 100</span></div>' +
          '<div style="margin-top:6px"><span style="display:inline-block;background:' + grade.color + ';color:#ffffff;font-size:13px;font-weight:bold;padding:4px 12px;border-radius:999px">' + esc_(grade.label) + '</span></div>' +
          '<div style="font-size:12px;color:#6b7280;margin-top:10px">' + esc_(explanation) + '</div>' +
        '</td></tr>' +
      '</table>' +
    '</td></tr>' +
    // Every attempt
    '<tr><td style="padding:8px 28px 4px"><div style="font-size:15px;font-weight:bold;margin-bottom:6px">What the learner did</div>' +
    '<table role="presentation" width="100%" style="border-collapse:collapse;font-size:14px">' +
    '<tr><th align="left" style="border-bottom:2px solid #e5e7eb;padding:8px 6px;color:#6b7280;font-size:12px;text-transform:uppercase">Scenario</th>' +
    '<th align="right" style="border-bottom:2px solid #e5e7eb;padding:8px 6px;color:#6b7280;font-size:12px;text-transform:uppercase">Score</th></tr>';
  for (var h = 0; h < rows.length; h++) {
    var g = gradeFor_(rows[h].score);
    html += '<tr><td style="border-bottom:1px solid #eef0f2;padding:10px 6px">' + esc_(rows[h].label) + '</td>' +
      '<td align="right" style="border-bottom:1px solid #eef0f2;padding:10px 6px;white-space:nowrap">' +
      '<b style="color:' + g.color + '">' + rows[h].score + '</b> <span style="color:#9ca3af">/ 100</span></td></tr>';
  }
  html += '</table></td></tr>' +
    // Footer
    '<tr><td style="padding:20px 28px 26px"><div style="border-top:1px solid #e5e7eb;padding-top:14px;font-size:12px;color:#6b7280">' +
      esc_(footer) + '</div></td></tr>' +
    '</table></div>';

  return { subject: subject, text: text, html: html };
}

/** Grade name and colours for a score. Same bands as the game: 85, 70, 50. */
function gradeFor_(score) {
  if (score >= 85) return { label: 'Road Ready', color: '#1b7a45', soft: '#e7f5ec' };
  if (score >= 70) return { label: 'Almost there', color: '#a16207', soft: '#fdf6e3' };
  if (score >= 50) return { label: 'Needs practice', color: '#c2410c', soft: '#fdeee4' };
  return { label: 'Unsafe - try again', color: '#b91c1c', soft: '#fdeaea' };
}

// ------------------------------------------------------------ sending

/**
 * Sends through Brevo when the script property BREVO_API_KEY is set, otherwise through Gmail.
 * The Brevo key lives ONLY in Project Settings > Script Properties, never in this file and never in the game.
 * Script properties used: BREVO_API_KEY and BREVO_SENDER_EMAIL (a sender you verified in Brevo).
 */
function sendEmail_(to, email, props) {
  var apiKey = props.getProperty('BREVO_API_KEY');
  var senderEmail = props.getProperty('BREVO_SENDER_EMAIL');
  if (apiKey && senderEmail) {
    var response = UrlFetchApp.fetch('https://api.brevo.com/v3/smtp/email', {
      method: 'post',
      contentType: 'application/json',
      headers: { 'api-key': apiKey, accept: 'application/json' },
      muteHttpExceptions: true,
      payload: JSON.stringify({
        sender: { name: SENDER_NAME, email: senderEmail },
        to: [{ email: to }],
        subject: email.subject,
        htmlContent: email.html,
        textContent: email.text
      })
    });
    var code = response.getResponseCode();
    if (code < 200 || code >= 300) {
      console.error('Brevo refused the email: ' + code + ' ' + response.getContentText());
      return false;
    }
    return true;
  }
  GmailApp.sendEmail(to, email.subject, email.text, { htmlBody: email.html, name: SENDER_NAME });
  return true;
}

// ------------------------------------------------------------ small helpers

/** Escapes the five characters that matter in HTML, so a name like <script> shows as plain text. */
function esc_(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/** Removes line breaks and other control characters, trims, and cuts long values. Empty becomes "(no name)". */
function clean_(value) {
  var s = String(value == null ? '' : value).replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ').trim();
  if (s.length > TEXT_LIMIT) s = s.substring(0, TEXT_LIMIT).trim();
  return s.length === 0 ? '(no name)' : s;
}

function clampInt_(value, min, max) {
  var n = Math.round(Number(value));
  if (isNaN(n)) n = min;
  return Math.max(min, Math.min(max, n));
}

function formatDate_(iso) {
  var d = new Date(String(iso || ''));
  if (isNaN(d.getTime())) return 'unknown';
  return Utilities.formatDate(d, 'UTC', 'yyyy-MM-dd HH:mm') + ' UTC';
}

function durationText_(seconds) {
  return seconds < 60 ? seconds + ' s' : Math.floor(seconds / 60) + ' min ' + (seconds % 60) + ' s';
}

function isAllowed_(address) {
  for (var i = 0; i < ALLOWED_RECIPIENTS.length; i++) {
    if (String(ALLOWED_RECIPIENTS[i]).trim().toLowerCase() === address) return true;
  }
  return false;
}

/** Compares two strings without stopping at the first difference (so timing does not give the secret away). */
function safeEquals_(a, b) {
  if (a.length !== b.length) return false;
  var diff = 0;
  for (var i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

/** Forgets session ids older than KEEP_SENT_IDS_DAYS so the stored list does not grow forever. */
function pruneOldIds_(props) {
  var cutoff = new Date().getTime() - KEEP_SENT_IDS_DAYS * 24 * 3600 * 1000;
  var all = props.getProperties();
  for (var key in all) {
    if (key.indexOf('sent_') === 0 && Number(all[key]) < cutoff) props.deleteProperty(key);
  }
}

function json_(object) {
  return ContentService.createTextOutput(JSON.stringify(object)).setMimeType(ContentService.MimeType.JSON);
}

