// QuickRun's web components, for any page that wants a Run button of its own.
//
//   <script type="module" src="https://quickrun.org/components.js"></script>
//   <quickrun-btn repo="acme/app">Try it</quickrun-btn>
//
// One file, no build step, no dependency. Served from quickrun.org and always the current one:
// a page embedding this gets today's behaviour rather than whatever was current when it was
// written, which matters because what happens on a click has already changed twice.
//
// What a click can and cannot do is the whole design. A page may ask whether QuickRun is there -
// /api/ping answers anyone on purpose - and it may hand a repository over. It may not start
// anything: the plan appears in QuickRun's own window and waits for a person there. That is why
// there is no component here that draws a plan or a log. One that did would be the convincing fake
// the confirmation window exists to prevent, and a page that could be talked into approving
// commands is the one thing this project must never ship.

const DEFAULT_PORT = 9876;
const RUN_PAGE = 'https://quickrun.org/run';
const DOWNLOAD_PAGE = 'https://quickrun.org/download';
const BADGE = 'https://quickrun.org/badge.svg';

/** Long enough for a loopback answer, short enough that a page is not held up by it. */
const PING_TIMEOUT = 1500;

/** A config comes from the internet rather than from loopback, so it gets longer - but not for ever. */
const CONFIG_TIMEOUT = 6000;

/**
 * What the button carries, and why it is in the file rather than fetched.
 *
 * The logo is the default, because a button on somebody else's page is QuickRun's face there and a
 * bare triangle is nobody's. It is embedded as data rather than pointed at quickrun.org: a page
 * with a strict img-src would show a broken image, a page loaded offline would show nothing, and
 * neither is worth a request per button. 64px, which is 2x for the size it is drawn at.
 *
 * The triangle is still here for anyone who wants one colour that follows their text -
 * glyph="play" - because a full-colour logo does not sit well in every design.
 */
const LOGO = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAABStSURBVHhe7VkHVFZnmp7ZOVtnd2bOlpnds2d36iZxY6ImaiwooSO9gy12jYjYNYoNBZEmvXekS/uRjiCgBBVEUYqisaBijQVEuO93n/vuuf+PG4fdzDhnkkkyh+ec9/Dz893vfk95v1v43vfGMIYxjGEMYxjDtwnM/IPR3/3ZQ7klTcB1hL7okk8/OSeuPG2TW5+cRfTTczR19Ng/K/B1/onSixDpqizzdeaBc8yPTjE/atZ9vt+s8P1mxPY19v/L6GO/81BuKy64iWt8m/lxC/jOCcKtesLNOgnXayVcrSZcrZb58RnmOyfk2/ea5QWj5/hOQrk2/CZ65WK+wzzcxXzvJOFOA6H3OOFmLeF6jYRrNSp5wuUKQlcZ4doxNQ2qENDcblTeGD3ndwKcxz9QeuWt8nUM8E3mh6eJ+1TXj0vorZVw45ikJf9ZlYSrlRJ6KgjdZYTOoxIulhDai0m53ch8vU4e6G2UtzLzX4w+x7cWynVhoPSihfuY+8/pXL9d/9J1CTdqJFyvJlxTY19J6KmQcKlMQlcp4eJRCe3FEs4VSjhbIOFCCfOtBuartTh9s16ZNfpc3yrwnf5/Vm4iSrnBjCvMD5oJfY2q67pe10VeJa46T7haRegpJ1wuJ3SXEjpKJFzQkI58voSWPAln8gincgld5czdFcw9lSKy91PlH0ef+xuHckteqPTiFt9jftoq892RXlcjr7quxl1LvorwWaUaex35S2WE7qMSOksIF4oJ54sktBVIaD1COJMnoTlHwqc5hKYsQlO2zD1VzJ1luNlTKc8fvYZvBIqi/HXDpyKruoE5u4C5upCg9vrDkyORV3d5daNTXR+J/JWKEfKlhK6jhE6NhIsaQnuRGn1Ca77O+dO5hOZcHfnGwxLq0yUcT1cTwdxVxtxZipLuCuXN0Wv6k6KkgXKPNDAHpwv2SyR4hQsExwg0awQenCD01qk7/IjjL4mXqeQldB2V0KGSL5a07p8rkHD2iC76p3MIzdkSmrIJjRkS6tMkHE8j1KUQalMI1cmktBYwtxTIz9s18o5v5I6yvk2YaJqY4/KFEpVDCDssEJhI2B0psPGgQEKcQFe5wJUyCZe1pev17qNfOH+hSEJ7oYTzBRLaRvr+tBr7LAknM18hnyqhLkXCsRRCTTKhMkFCWZyE6iSFzxYxn8mXW9uLafboNX6tKD0hYgoamRMKCTG5qgCEoGSCf4LA3kiBFbsF4g4LPDwn0FUsoVOtEkJHsc71i8WkJX8uX+d86xFd9LXOZxFOZEhoUGOvJU84liShKkFCZQJpyZfEEDTRhMJowrF05oZM5uZchHcXK/8weq1fCzQNclFuLXN8PiE6lxCeSQhOIRyMJ+yPFthwUCC5QEAZkvHwskBPFeFCPuHiiOvthaQjnyehVd3x1Z7Xkte5r5JvSBXy8RSSa0bIV8RJKItVyUsoipSQHyEhL5yQE0bIi5D5eCbzsTT5YkP2i/8Yvd6vHMX1clFOLXPsEUJkti4Bwak6AbyjCJsCBNKKBJTnMqQnMgYfyLh1htBdAlwuU9B2REe+JZdwOpvQnCmhKUPCicMjzqcRjqfqnK9OlFARL6E0VsLRWEJxNCE/XEJuqITsUEJmCCEjhJB2iFCWylwYJ05+7TdPRfVycVYNc3QeISKbEHKYEJhM8Isn7I0ibAwUSC/WCTD0UMbwYwF5EMgOvYqMfd3oKVNwLl/G6Wzp/5CvV8mnSKhNllCjko/TkddESyiKUp0n5IZJyAoRcmaIkNMP6cinBBESg0gpSGROjhLWo9f8laKgVi7OrGKOUvtfjX8awT+J4BtH8I4WWgHSVAEGBIYeCgw9ElCGFUTsPoeZvwyHz4omnMp8jo4SBZ9mEk6okU+TUJ8qacnXJZOWfGX8F7EvjiT5pfNZIRIyDhHSg3XEk4MICYGE+EBCZgxzQIhIGL3m14IaHUVR/vZlMfPfvPz86rj8Y3Lx4UrmyBxCaAYhMJXgl0DweUWAVLUFBgRePBgR4IWCJP8umL+bBvsp+fjISIP0/VdwvlDBqWxoiR9PllCXJKEmQUJVvOo+oVQlHyWhIIJwJExCtpa8pCMfSEgM+IJ8XBAhLYp5a7Bc9ep6XwuDHbR26KJy+d6n4tatk6L3xklx83KDuNleK3pbauRbx0rlqsoiRbvB5NbImrQK5vAswqF0gn8ywVft/2iCVwRhnb9ASqGuBVQBXjwUUAZVATphNSkTy0yPY/6HVbCZkovNc2tRHf8ErUcU1KcIHEuQUB0voTxWQlm0BE2khEKVfDghV+35YJW8hJQAQqI/Id6fEBegq5RgQkg482J/uWY0v9+J4UvDbz1Tn8frmC+VMbeXMJ8uZG7IZ67IYc7PYi4sYg4MF3Hq+NxquSi1nDk0kxCQSjiQSNgXQ9gVSdgRTvA8OCLAgMCgVgBZJ0BgFyzfy8ZS03osN2/EEtPjsJ2aD4eZWQjd1I6mDBkn0hVUxhHKYiSUqJe6CEnrfF4YISuEkB70hfNa8v6EBFWMIMIBP8KKA8yLA/9AAfqbh8ddK2M+m8N8PIW5PJG5II45PZo5JoI5KJz5YAzzmp0iQh2fVSUXJ5cxHzpM8E/RCeAdo3P/kzDC2peXwf5XBFBbIOgLAZbNOYGl5o1YaFgHhxkamExKwzJLDXID7qLpsIKKWKF1v0Dt+0MSsg8RDo+QT1JJj7ifFEgIP0hYv1vAdSth8X7m5QGoHs3x96IlnTxrY5Xu7CBxLdFPXA31FdcO+ojPdu0Xn23ehyurvJBvvUz5mTo2s0rWJB5lDkwj+CURfBIIu6N15LeGENx9BRLzBdAvMHBfYFAV4LlOAIv3srDErB5LzRvgqncMyy3r8ZFJFWymFmDOlEyYvp+IvStOoTKGUBmn4EgoaclnBBNSAyQkHSQkHCQkq64HEnZ5CyzcQnDdTJi3jbDUh3mpv/yHC/ASzt9T763V6+j//lTr+6+OySiXNfEa5oC0EfIxhB2RhG1hhM3BhFX7CAlHvhDg+YMRAYI7YT4pA4vMjsNNvxZZsT0YfKLg81vDCNhyAhbvHYbNzFzovxsHZ4McxO28gYo4BXkhQHqAhBR/QrI/IS2QEOwrsHyLgMM6gttGwrw/RgBm/r5ao7//MqSVUklc8YgA8YRdqvvhOvc3BhFWeBPi81QBZK0AAw8EMKAgMbgTZhMz4DSzCv5b26AIBanhrWgsv4L+ewLzTTJhMSUDtrPyYPBeEmaOD4enay1ygl6gIEJBZhAh1o+waYeAk6eA41qC63qdAG6bCHPVFvBhXvK7BHjeIS0aaMPpuydE1+0G0XnzuOi6Wie6L9WISx3Vovt8lbjUUi4uNZXKlxtLRU9tidxTXaKcz8+WPV7OkVIqa6ILmQ8kEbzjCF5RhG2hhE3BhA2BhOXehDitALoEvBQgIagDppPSYTW5FPnJVzD0VMGkn3vDfcFhrRirXPJg+G48bGfnw3rWEZhNy8C0t8NhPi0ZmzyvY9teBfM9CbZrCM5rCS6eBJd1BNcNBJeNBJfNAgu9mRf5fYkAA+3SO09OMffVMV8uZe7QMJ8rZj5TyHwij7k2h7kyh1mTyZyXzpyRxpyUzJyQwhyXwuwTrExT50k5iuLIAmafRMKuGMInEYTNIYQNQYR1AYRle3UCyP1C62z/fQH5uYKE4E6YTEqH/YwybFvRgOFnQGfLAzy4/QItjY/x4YRAmE1JgcXMPBhMysLsiRkw+SATsybEYOq4Q7BddAWuGxU4eQzD2ZO05bROV85aAQjz9zIvPPAlAjw8KQzu1jB/mq7w8STBlXEya2IEH4kSnBkpc1K4zDFhMoeHyhx4SGbfYJm9g8E7A5l3hTK77xQW6jxJGlkTkc+8P4HgFU3YFk7YdIiwPpDg6U9YsksgJkcnwDNVgHsC8oCC+KAOGE1Mg5tRFSw/KIbXmloMP1XQUNGHDycdxIxxATCenA47w3z472rC3o11MHk/STaYmCTrT4jBh5MT4Lq2H05rCY4ew3D0JF2tIziuJzhtIszbwzzf50sE6Mjjv/o0mQ5XR0IUBMuc4U+cdFBwtC8pIT6k+O8n7N9P8NpH2LJXe2mRPXYJ2X0Phj/aLLLNzZW/VudJ0MiasCPM++IJO9T4hwttAtYFEtb4ET7aKRCdIyCeyXh6TxVBhhhQEBt4EYYTU+FiVAkng0pYTs9D37VhhB84iXH/5gXjKSlwMMnFpQuPgBcKlCEFpTmXMXt8OPQnJch6b4fByqkNTusV2K8Zhv1aCfZrSVeeBPv1hLm7mRf4/p77gMKAoV9n7JPeid8nvR25U3o7cM/wf+/bOTxuz/bht7ZvH35r4/bhtzy2D7+1/BPljcVblP9a6Mn/+erx8RpZE5rHvDeO8EkkYUu4uvkJWXXf/QBhoZdAZNZLAXQiSP0KYoM6YDgpDS7GVXAyqoaLaRnuXpcQH3IWU94Mwcx3UnA4vh3PHyswnxkC781FwKCCxfYZmPpGKPTGR8DEtAYO6wBb9xew9ZBg60Gw8SDYehLs1hNcdjK77fs9AvyxiCmkkpBc5l1xhG2RhE2hOvc9/AmrDwgs8CKtAPRMxpO7QlvSMwXRgR3Qn5gOJ+MqOBhWw9m0HHdvEBJC2vDBuHDMeDcdKdHt2vFz9Pzhs71I2yIuc5Iw+TdB0BsfCWPjKth5AtarX8BmzTCs3Elb1msJ1uqmuJvZxRvpo9f82nh8nvQetym7rp2U93TVyXvO1Yi9p6qV/dUaee7LMTGFsiY4h9krRuf+hhDC2gDCaj/Cx1oBBCKyBKSnMh73CW0NP1MQGdCB2VoBqmFvVA0nswqdAKFtmDouDFPHpSA+vBWKpGDwkaK9SsSHXcD4n++A3rtR0BsfBSPjatisBSw/fgHL1UOwWC3Bwp1g6UGwWkuKy15mVx/Z+bdZvSY+P0vT7zUp3NfEfKmO+Ww1c+NR5tJC5px85gOhukuhKkBgDvOOaJ0A64N17n98gLDKV2D+DoGITAHpicDnd1QBZC2ZyIBOrQCOJtWwM6qCo1k57l7XCfCbn/ohcH8TMKwgKqgV7otysGxuDt7+xSeY/KY/Zr+XCL13omFgVAPLNQLmqwZh/vEQ5qyWMEf9fQ3B/hNmey/RsjKu9S9Hc3st3K6TFt2uY65OYS5KYE6LYY4IZ/Y9xLw3knnlNhGljosqlDUB2czbowmbwwjrgglrtAIIrPQRcNsmEJquE+DRHYHP+4RWgGDfi5g1MR0OptWwM66WLWaX4vZnEg75NGPDxwVQFAXRIW34+T954Nc/3Yrxv/DBtPERmP1eEma/n4qZ78bCwPgY5rgLmK4ahNnqYZitJpi5g+1U8jvkHqf9Q78czeu10VOu/Kg6Vi48HIQHIQfEg3376P6WPeKex27cX7QVjdbLdZNH5MvFB7OYt0WNxD+I4O5PWOlDWOZNmPeJwNYQgcHPZfSr7wOeKui7rmC+QxkMpuTCzrgGxtNLsXtLs3Zz/KxzAI/6JEQGn8WvfrYB77/hj9mTEqE/OR36UzKgPyUT+pMzoTchEQbGtZizRobpqiGYfEywWM9svZXZfifinfY8/Wr+c3Rw2+Mfr1v35CcrVz7+8fy1yo/UevXv4XlysV8m89ZInfsq+VUHCMv3CyzzFljspbsrS82XcfeWgvazL+CxogEfvJOCOfplMJlRCc+VJzH0TMbRvBtoO/kAvT0SZkz0w8Rf+0N/Sio+nJoF/ak50P8gF/of5EF/ai70JiTDwKQBZu4KTN2ZrbcwW21Fi4u3MHh1fV87QvOo+EAm8+YIgueIAMtV9/cJLNktsMiLsHCHDKfNwDz3RzAxasC09/Jhql8OC4Ma6L1ficzkq3h6H/jVv+6Am02s9j5hkVMu3n8rEgbTc2AwIx8G0wtgMKMQH04v0Iowa2IyjG072GoLs+VG+am9l7J5z9f9AvT/Q0iuiPLLZt4QRvAIIqw+qLpPWLKXsHgXYbG3jAXbJDguvAcLsw5YmLbByrwFlqZNsDRphMH0Y9i15SzkQQWlR7px/vQdXOkgGEyLxvSJKTCYWQBDPQ0M9Uq0ZTBTA/1p+TDWr2PbdWqvK1kOe4Z+NXpdfzJE5AszNQHrD5Gi7X1fwtK9hKVq/PcKzPUYhIPzQzjY98HB4RZsba/Cxrob1hbnYWV+RiuE8axqhPm1o+f8EOqrHsHZJhMT3jikdd5oVimMZpfDaFY5DGaWwnBWBZsbnWHrj+52uXgrX+8b39eB+ujsnSzX7Elldg8grPRTn8MFFm4jOC96AQen53B07Yejy2M4Ot+HvUMvbO2uwcb6Eqwt2mE1pxUWJs3Qm1oG/Wk5eH98NCa8FQH96XkwnHUURvpVMNavhvHsKp5j3MLmJmcHrR1v7NqQ+9svZ79R+CYM/Gx7JFo3RzAv2w2epz6izpPg6EZwmifBae4QnNz64ej6ORyc7sLO/iZsba+MJOECrOa0wdL0FEwM6mA4uxJG+mqpxGu0ZW7YxJZmF9ja4nKRq2v/uNHn/1Zg5R7+O3cvhC7xZJ6/jNlloaQ4LyRoa74qwiCc3J7Awfk+bNVWUFNgcxk2Vl2wsrgAS/M2zDE9DVPDkzA1bISpYQPMDE+wjUU321hc7nawvWc3+pzfSmzwJf0VW3BqqSez2xJml0U6ERznDsPBrR8OLo9g53QHttoUXIW19SVYWnbC0uICLMzPwdykBeYmzYqNxSW2sugecrC7s2/BAuWHo8/zrYa6L6zZQeuXrseTRauZnRcKdlogwWHuc9i7Poa9yz3YOvbC2vYarG16RkTowBzz87C2vMT2trfY3q6vxNFNenv03N8pbApUfrlsA3IXuevS4DD3BRzmPoO960PYOt2Btd11WNlchZX1ZVhb97CD40O2t++74uTy1G30XN9puG9TbD9yR/eClcyO84bZzuUxbJ3vwdr+Jqxsr7G90+ds5/hg2Mntma+7O//96ONfE6/9IvcbwaZA5YeL18Bv3lLx1G2xukkyO88XbO/yjB1cBo46uinvjj7mzxKrNgz++4Ll8nzXRcNerh+Rh9tCZfLoMWMYwxjGMIYxjGEMYxjDV4//AfSa3wW9KP6MAAAAAElFTkSuQmCC';

const GLYPHS = {
  logo: `<img src="${LOGO}" alt="" draggable="false">`,
  play: '<svg viewBox="0 0 10 10" aria-hidden="true">'
    + '<path d="M0.5 0 L9 5 L0.5 10 Z" fill="currentColor"/></svg>',
};

/** The mark as it sits in a shadow root: a named part, whatever is inside it. */
const MARK = '<span part="icon" class="icon"></span>';

/**
 * Whether QuickRun answers on a port, asked once per port for the whole page.
 *
 * Every button and every status badge wants the same answer, and a page with a list of
 * repositories would otherwise open one connection per row.
 */
const asked = new Map();

function ping(port) {
  if (asked.has(port)) return asked.get(port);

  const answer = (async () => {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), PING_TIMEOUT);

    try {
      const response = await fetch(`http://127.0.0.1:${port}/api/ping`,
        { cache: 'no-store', signal: stop.signal });

      if (!response.ok) return { running: false };

      const body = await response.json();
      return { running: true, version: body.version, busy: Boolean(body.busy) };
    } catch {
      // Nothing listening, or a browser that refused the request. Either way: not running.
      return { running: false };
    } finally {
      clearTimeout(timer);
    }
  })();

  asked.set(port, answer);
  return answer;
}

/**
 * What a hand-over may name, as a query.
 *
 * Never commands. A config is named - by a path inside the repository, by an address, or by the
 * word "collection" for the one QuickRun keeps - and QuickRun reads it itself, so what runs is
 * always a file somebody can look at rather than a string out of a link.
 */
function carry({ repo, ref, pr, config }) {
  const parts = [`repo=${encodeURIComponent(repo)}`];

  if (ref) parts.push(`ref=${encodeURIComponent(ref)}`);
  if (pr) parts.push(`pr=${encodeURIComponent(pr)}`);
  if (config) parts.push(`config=${encodeURIComponent(config)}`);

  return parts.join('&');
}

/**
 * Hands a target to the QuickRun on this machine.
 *
 * Two ways, tried in this order for a reason. A site the reader has trusted may ask the local
 * QuickRun to open its window, which is seamless - quickrun.org is trusted out of the box, and
 * anyone can add their own site in QuickRun's settings. Everywhere else the quickrun:// scheme does
 * the same job, at the cost of one "open QuickRun?" from the browser. Both end at the same window
 * with the same plan waiting to be approved; the difference is only who is asked first.
 *
 * @returns 'window' when QuickRun opened it directly, 'scheme' when the browser was asked to.
 */
async function handOver(target, port) {
  const query = carry(target);

  try {
    const response = await fetch(`http://127.0.0.1:${port}/api/show?${query}`,
      { method: 'POST', mode: 'cors', cache: 'no-store' });

    if (response.ok && (await response.json())?.shown === true) return 'window';
  } catch {
    // Not a trusted site here, or an older QuickRun. Neither is an error - it is the answer.
  }

  location.href = `quickrun://run?${query}`;
  return 'scheme';
}

const STYLE = `
  :host {
    display: inline-block;

    /* Everything worth changing without writing a selector. A page that wants more can reach the
       parts - ::part(button), ::part(icon), ::part(label) - or turn the styling off entirely with
       the unstyled attribute and dress the markup itself. */
    --quickrun-bg: #6d4ac4;
    --quickrun-fg: #fff;
    --quickrun-border: transparent;
    --quickrun-radius: 8px;
    --quickrun-padding: 8px 14px;
    --quickrun-gap: 8px;
    --quickrun-font: inherit;
    --quickrun-size: 14px;
    --quickrun-icon-size: 1.15em;
    --quickrun-weight: 600;
  }

  :host([hidden]) { display: none; }

  /* A triangle at logo size looks like a mistake. An inline style on the element still wins. */
  :host([glyph="play"]) { --quickrun-icon-size: 0.85em; }

  button {
    display: inline-flex;
    align-items: center;
    gap: var(--quickrun-gap);
    font: inherit;
    font-family: var(--quickrun-font);
    font-size: var(--quickrun-size);
    font-weight: var(--quickrun-weight);
    line-height: 1.2;
    padding: var(--quickrun-padding);
    border: 1px solid var(--quickrun-border);
    border-radius: var(--quickrun-radius);
    background: var(--quickrun-bg);
    color: var(--quickrun-fg);
    cursor: pointer;
  }

  button:hover { filter: brightness(1.08); }
  button:active { filter: brightness(0.94); }
  button:focus-visible { outline: 2px solid var(--quickrun-fg); outline-offset: 2px; }
  button[disabled] { cursor: progress; opacity: .75; }

  /* The icon after the label rather than before it, without the markup changing order - so a
     screen reader reads the same thing either way. */
  button[data-icon="right"] { flex-direction: row-reverse; }

  .icon {
    width: var(--quickrun-icon-size); height: var(--quickrun-icon-size);
    flex: none; display: inline-flex; align-items: center; justify-content: center;
  }

  .icon img, .icon svg { width: 100%; height: 100%; display: block; }
`;

/**
 * A Run button for a repository.
 *
 * Attributes: repo, ref, pr, run-cfg ("collection", a path inside the repository, or an https
 * address), icon (left | right | none), glyph (logo | play), label, mode (window | link), port,
 * unstyled.
 */
class QuickRunButton extends HTMLElement {
  static observedAttributes = ['label', 'icon', 'glyph', 'unstyled'];

  #button;
  #label;
  #style;

  constructor() {
    super();

    const root = this.attachShadow({ mode: 'open' });

    root.innerHTML = `<style>${STYLE}</style>
      <button part="button" type="button">${MARK}<span part="label" class="label"></span></button>`;

    this.#style = root.querySelector('style');
    this.#button = root.querySelector('button');
    this.#label = root.querySelector('.label');

    this.#button.addEventListener('click', () => this.run());
  }

  connectedCallback() {
    this.#render();

    // What the button says before anything is known: the label, and no promise about QuickRun
    // being there. Whether it is decides only what a press does, not whether the button works.
    ping(this.port).then((state) => {
      // On the host rather than inside, so a page can style the button for either case:
      //   quickrun-btn[data-running] { ... }   quickrun-btn:not([data-running]) { ... }
      if (state.running) this.dataset.running = '';
      else delete this.dataset.running;

      this.dispatchEvent(new CustomEvent('quickrun-status', {
        bubbles: true,
        detail: state,
      }));
    });
  }

  attributeChangedCallback() {
    if (this.shadowRoot) this.#render();
  }

  get port() {
    return Number(this.getAttribute('port') || DEFAULT_PORT);
  }

  get target() {
    return {
      repo: this.getAttribute('repo') ?? '',
      ref: this.getAttribute('ref'),
      pr: this.getAttribute('pr'),
      config: this.getAttribute('run-cfg') ?? this.getAttribute('config'),
    };
  }

  /**
   * Does what a press does, so a page can trigger it from its own control.
   *
   * The event goes out first and can be prevented: a page that wants to ask something of its own
   * before QuickRun opens - a licence, a warning, its own dialog - has somewhere to do that.
   */
  async run() {
    const target = this.target;

    if (!target.repo) {
      this.#say('no repository');
      return;
    }

    const event = new CustomEvent('quickrun-run', {
      bubbles: true,
      cancelable: true,
      detail: { target },
    });

    if (!this.dispatchEvent(event)) return;

    if (this.getAttribute('mode') === 'link') {
      location.href = `${RUN_PAGE}?${carry(target)}`;
      return;
    }

    this.#button.disabled = true;

    try {
      const state = await ping(this.port);

      // Not installed: the run page explains what to install and carries the repository with it,
      // so the press is not lost on the way.
      if (!state.running) {
        location.href = `${RUN_PAGE}?${carry(target)}&executeQuickRun=true`;
        return;
      }

      const how = await handOver(target, this.port);

      this.dispatchEvent(new CustomEvent('quickrun-handover', {
        bubbles: true,
        detail: { target, how },
      }));
    } finally {
      this.#button.disabled = false;
    }
  }

  #render() {
    // Decided here rather than in the constructor: a page that builds a button with
    // createElement - which is what every framework does - sets its attributes afterwards, and a
    // constructor has already run by then. Reading it in the constructor worked in hand-written
    // markup and silently did nothing everywhere else.
    const styled = !this.hasAttribute('unstyled');

    if (styled && !this.#style.isConnected) this.shadowRoot.prepend(this.#style);
    if (!styled && this.#style.isConnected) this.#style.remove();

    const icon = this.getAttribute('icon') ?? 'left';
    const glyph = this.getAttribute('glyph') === 'play' ? 'play' : 'logo';
    const mark = this.shadowRoot.querySelector('.icon');

    if (mark) {
      // Only when it changed: replacing an <img> on every render would reload it.
      if (mark.dataset.glyph !== glyph) {
        mark.innerHTML = GLYPHS[glyph];
        mark.dataset.glyph = glyph;
      }

      mark.hidden = icon === 'none';
    }

    this.#button.dataset.icon = icon;

    this.#label.textContent = this.getAttribute('label')
      ?? (this.textContent.trim() || 'Run this');
  }

  #say(text) {
    this.#label.textContent = text;
  }
}

/**
 * Whether QuickRun is on this machine, as a line of text.
 *
 * Built on the one endpoint that answers any page on purpose, and it reveals nothing else: no
 * repository names, no paths, no runs. A page can use it to decide between "press Run" and "get
 * QuickRun first" rather than finding out after the click.
 */
class QuickRunStatus extends HTMLElement {
  connectedCallback() {
    // Moving an element in the DOM disconnects and reconnects it, and a second attachShadow on the
    // same host throws.
    if (this.shadowRoot) return;

    const root = this.attachShadow({ mode: 'open' });

    root.innerHTML = `<style>
      :host { display: inline-block; font: inherit; }
      .line { display: inline-flex; align-items: center; gap: 6px; }
      .icon { width: var(--quickrun-icon-size, 1.1em); height: var(--quickrun-icon-size, 1.1em); }
      .icon img, .icon svg { width: 100%; height: 100%; display: block; }
    </style><span part="status" class="line">${MARK}<span part="label" class="label">…</span></span>`;

    root.querySelector('.icon').innerHTML =
      GLYPHS[this.getAttribute('glyph') === 'play' ? 'play' : 'logo'];

    const label = root.querySelector('.label');

    ping(Number(this.getAttribute('port') || DEFAULT_PORT)).then((state) => {
      label.textContent = state.running
        ? (this.getAttribute('running') ?? `QuickRun ${state.version ?? ''}`.trim())
        : (this.getAttribute('missing') ?? 'QuickRun not running');

      this.dataset.running = state.running ? '1' : '';

      this.dispatchEvent(new CustomEvent('quickrun-status', { bubbles: true, detail: state }));
    });
  }
}


/**
 * The README badge, as an element, for a page that wants the badge look and a direct hand-over.
 *
 * The image is the same one a README carries, so the two look identical wherever they sit side by
 * side. What is different is the click: mode="run" hands the repository to the QuickRun on this
 * machine the way the button does, while the default stays a plain link to quickrun.org - which is
 * what a README has to use, because GitHub runs no scripts.
 */
class QuickRunBadge extends HTMLElement {
  connectedCallback() {
    if (this.shadowRoot) return;

    const root = this.attachShadow({ mode: 'open' });
    const port = Number(this.getAttribute('port') || DEFAULT_PORT);

    const target = {
      repo: this.getAttribute('repo') ?? '',
      ref: this.getAttribute('ref'),
      pr: this.getAttribute('pr'),
      config: this.getAttribute('run-cfg') ?? this.getAttribute('config'),
    };

    root.innerHTML = `<style>
      :host { display: inline-block; line-height: 0; }
      a { display: inline-block; }
      img { display: block; height: var(--quickrun-badge-height, 20px); width: auto; }
    </style><a part="link"><img part="image" alt="QuickRun"></a>`;

    const link = root.querySelector('a');
    root.querySelector('img').src = this.getAttribute('src') ?? BADGE;
    link.href = `${RUN_PAGE}?${carry(target)}`;

    if (this.getAttribute('mode') !== 'run') return;

    // A hand-over rather than a page in the middle. The link stays what it is underneath, so a
    // middle click, a copied address and a page with no scripts all still work.
    link.addEventListener('click', (event) => {
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return;

      // Stopped here and now, before anything is awaited: a preventDefault after the first await
      // arrives long after the browser has left, which is exactly what it did.
      event.preventDefault();

      const asked = new CustomEvent('quickrun-run', {
        bubbles: true, cancelable: true, detail: { target },
      });

      if (!this.dispatchEvent(asked)) return;

      (async () => {
        const state = await ping(port);

        // Not there: the link was right all along, so go where it points.
        if (!state.running) { location.href = link.href; return; }

        const how = await handOver(target, port);
        this.dispatchEvent(new CustomEvent('quickrun-handover', {
          bubbles: true, detail: { target, how },
        }));
      })();
    });
  }
}

/**
 * Two slots, and the ping decides which one a reader sees.
 *
 *   <quickrun-gate>
 *     <p slot="running">Press Run and it starts here.</p>
 *     <p slot="missing">Get QuickRun first - it is one download.</p>
 *   </quickrun-gate>
 *
 * Everything else on the page can then be written for one case rather than hedged for both. What it
 * knows is what /api/ping tells anybody: whether something answers, and its version.
 */
class QuickRunGate extends HTMLElement {
  connectedCallback() {
    if (this.shadowRoot) return;

    const root = this.attachShadow({ mode: 'open' });

    // Neither slot until the answer is in: a page that flashes "get QuickRun" at somebody who has
    // it is worse than one that waits 200ms.
    root.innerHTML = `<style>
      :host { display: block; }
      slot { display: none; }
      :host([state="asking"]) slot[name="asking"],
      :host([state="running"]) slot[name="running"],
      :host([state="missing"]) slot[name="missing"] { display: revert; }
    </style>
      <slot name="asking"></slot><slot name="running"></slot><slot name="missing"></slot>`;

    this.setAttribute('state', 'asking');

    ping(Number(this.getAttribute('port') || DEFAULT_PORT)).then((state) => {
      this.setAttribute('state', state.running ? 'running' : 'missing');
      this.dispatchEvent(new CustomEvent('quickrun-status', { bubbles: true, detail: state }));
    });
  }
}

/**
 * The download, named for the machine reading the page.
 *
 * Always QuickRun's own download page - never a release asset directly: which file is right for a
 * machine is a question that page answers, and keeps answering after this one was written.
 */
class QuickRunGet extends HTMLElement {
  connectedCallback() {
    if (this.shadowRoot) return;

    const root = this.attachShadow({ mode: 'open' });

    root.innerHTML = `<style>${STYLE}
      a {
        display: inline-flex; align-items: center; gap: var(--quickrun-gap);
        font: inherit; font-family: var(--quickrun-font); font-size: var(--quickrun-size);
        font-weight: var(--quickrun-weight); line-height: 1.2;
        padding: var(--quickrun-padding); border: 1px solid var(--quickrun-border);
        border-radius: var(--quickrun-radius); background: var(--quickrun-bg);
        color: var(--quickrun-fg); text-decoration: none;
      }
      a:hover { filter: brightness(1.08); }
    </style><a part="button"><span part="icon" class="icon"></span><span part="label" class="label"></span></a>`;

    root.querySelector('.icon').innerHTML =
      GLYPHS[this.getAttribute('glyph') === 'play' ? 'play' : 'logo'];

    const link = root.querySelector('a');
    link.href = DOWNLOAD_PAGE;
    link.rel = 'noopener';

    root.querySelector('.label').textContent = this.getAttribute('label')
      ?? (this.textContent.trim() || `Get QuickRun for ${platform()}`);

    // A reader who already has it does not need a download button: a page can take it away, or
    // leave it and style it differently.
    ping(Number(this.getAttribute('port') || DEFAULT_PORT)).then((state) => {
      if (!state.running) return;

      this.dataset.running = '';
      if (this.hasAttribute('only-when-missing')) this.hidden = true;
    });
  }
}

/** What this machine is, as the download page names it. Good enough for a button's label. */
function platform() {
  const said = `${navigator.userAgent} ${navigator.platform ?? ''}`;

  if (/Mac|iPhone|iPad/i.test(said)) return 'macOS';
  if (/Linux|X11/i.test(said) && !/Android/i.test(said)) return 'Linux';
  if (/Win/i.test(said)) return 'Windows';

  return 'your machine';
}

/**
 * The config a repository would be run with, shown before anybody presses anything.
 *
 * Both places it can come from are public and readable from any page: the repository's own
 * quickrun.yml, and the one QuickRun keeps for repositories that ship none. So a page offering to
 * run something can show what that means, right there, instead of asking for trust.
 *
 * The text is somebody else's file, so it is written as text and never as markup.
 */
class QuickRunConfig extends HTMLElement {
  async connectedCallback() {
    if (this.shadowRoot) return;

    const root = this.attachShadow({ mode: 'open' });

    root.innerHTML = `<style>
      :host { display: block; }
      .from { font: inherit; font-size: .85em; opacity: .75; margin: 0 0 6px; }
      pre {
        margin: 0; padding: 12px 14px; overflow: auto;
        max-height: var(--quickrun-config-height, 320px);
        border: 1px solid var(--quickrun-border, #8883);
        border-radius: var(--quickrun-radius, 8px);
        background: var(--quickrun-config-bg, #7b61ff0f);
        font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
        font-size: var(--quickrun-config-size, 12.5px); line-height: 1.5;
      }
    </style><p part="from" class="from"></p><pre part="config"><code></code></pre>`;

    const from = root.querySelector('.from');
    const code = root.querySelector('code');
    const repo = plainRepo(this.getAttribute('repo'));

    if (!repo) { from.textContent = 'no repository given'; return; }

    const ref = this.getAttribute('ref') || 'HEAD';
    const named = this.getAttribute('run-cfg') ?? this.getAttribute('config');

    // Asked all at once and answered in order: the first place that has one wins, whichever
    // answered first. One after another meant a page waiting out every source that had nothing.
    const sources = places(repo, ref, named);
    const answers = await Promise.all(sources.map(([, url]) => read(url)));

    for (let i = 0; i < sources.length; i += 1) {
      const text = answers[i];
      if (text === null) continue;

      const [where, url] = sources[i];

      from.textContent = where;
      code.textContent = text;

      this.dispatchEvent(new CustomEvent('quickrun-config', {
        bubbles: true, detail: { repo, from: where, url, text },
      }));

      return;
    }

    // Not nothing: this is exactly the case where QuickRun reads the files itself and says in its
    // own window that what it found was a guess.
    from.textContent = `neither ${repo} nor QuickRun has a config for it - QuickRun would read the `
      + 'files and say in its window that it guessed';
    root.querySelector('pre').hidden = true;
  }
}

/** owner/name, however the repository was written. */
function plainRepo(value) {
  return (value ?? '')
    .trim()
    .replace(/^https:\/\/github\.com\//i, '')
    .replace(/\.git$/i, '')
    .replace(/^\/+|\/+$/g, '');
}

/** Where a config can be read from, in the order QuickRun itself would use. */
function places(repo, ref, named) {
  const raw = `https://raw.githubusercontent.com/${repo}/${ref}`;
  const collected = [`the config QuickRun keeps for ${repo}`,
    `https://quickrun.org/configs/${repo}.yml`];

  if (named === 'collection') return [collected];
  if (named && named.includes('://')) return [[`published at ${named}`, named]];
  if (named) return [[`${named}, in this repository`, `${raw}/${named}`]];

  return [
    ["this repository's own quickrun.yml", `${raw}/quickrun.yml`],
    ["this repository's own quickrun.yaml", `${raw}/quickrun.yaml`],
    collected,
  ];
}

/**
 * A config file, or null when there is none there.
 *
 * Never an exception into somebody's page, and never an open-ended wait: a source that does not
 * answer at all left the element showing nothing for as long as the browser cared to keep trying.
 */
async function read(url) {
  const stop = new AbortController();
  const timer = setTimeout(() => stop.abort(), CONFIG_TIMEOUT);

  try {
    const response = await fetch(url, { cache: 'no-store', signal: stop.signal });
    if (!response.ok) return null;

    const text = await response.text();
    return text.trim().length > 0 ? text : null;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

const ELEMENTS = {
  'quickrun-btn': QuickRunButton,
  'quickrun-status': QuickRunStatus,
  'quickrun-badge': QuickRunBadge,
  'quickrun-gate': QuickRunGate,
  'quickrun-get': QuickRunGet,
  'quickrun-config': QuickRunConfig,
};

// Defined once even where two copies of this file end up on one page, which is what happens when a
// site embeds a button and also a widget that embeds one.
for (const [name, element] of Object.entries(ELEMENTS))
  if (!customElements.get(name)) customElements.define(name, element);

export {
  QuickRunButton, QuickRunStatus, QuickRunBadge, QuickRunGate, QuickRunGet, QuickRunConfig,
  ping, carry, handOver,
};
