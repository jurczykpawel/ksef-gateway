"""Notify about upstream observations; never record them as installed versions."""
import json
import os
from pathlib import Path
import re
import subprocess

REPOS = {
    'sdk': 'CIRFMF/ksef-client-csharp',
    'docs': 'CIRFMF/ksef-api',
    'pdf': 'CIRFMF/ksef-pdf-generator',
}
STATE = Path('.github/ksef-versions.json')


def gh(*args):
    return subprocess.check_output(['gh', *args], text=True).strip()


def observe():
    snapshot = {}
    for name, repo in REPOS.items():
        commits = json.loads(gh('api', f'repos/{repo}/commits?per_page=1'))
        sha = commits[0].get('sha', '') if isinstance(commits, list) and commits else ''
        if not isinstance(sha, str) or not re.fullmatch(r'[0-9a-fA-F]{40}', sha):
            raise ValueError(f'Invalid {name} SHA: expected 40 hex characters')
        snapshot[f'{name}_sha'] = sha.lower()
    release = json.loads(gh('api', f'repos/{REPOS["sdk"]}/releases/latest'))
    version = release.get('tag_name')
    if not isinstance(version, str) or not version.strip():
        raise ValueError('Missing SDK release tag')
    snapshot['sdk_version'] = version
    return snapshot


def main():
    previous = json.loads(STATE.read_text())['last_notified']
    current = observe()  # Complete validation before any issue or state mutation.
    changed = [key for key in current if current[key] != previous.get(key)]
    if not changed:
        print('No changes since last notification')
        return

    body = 'Upstream changes observed (not installed or verified by the gateway).\n\n'
    for name, repo in REPOS.items():
        if f'{name}_sha' in changed or (name == 'sdk' and 'sdk_version' in changed):
            body += f'### {repo}\nhttps://github.com/{repo}\n'
            body += f'- Commit: {current[f"{name}_sha"]}\n'
            body += f'- Last notified: {previous.get(f"{name}_sha") or "none"}\n'
            if name == 'sdk':
                body += f'- Release: {current["sdk_version"]}\n'
            body += '\n'
    body += ('Review SDK, schema and PDF changes; update dependencies if needed '
             'and run integration tests against KSeF TEST.\n')
    body += ('\nJeśli wyszła nowa wersja SDK: wykonaj kroki z vendor/nuget/README.md '
             '(pobierz nupkg, zweryfikuj, podmień, zaktualizuj sumy SHA-256 w '
             'THIRD_PARTY_NOTICES.md i zatwierdzony manifest SDK, uruchom testy).\n')
    repo = os.environ['GITHUB_REPOSITORY']
    issues = json.loads(gh('issue', 'list', '--repo', repo, '--label', 'ksef-update',
                           '--state', 'open', '--json', 'number', '--limit', '1'))
    if issues:
        gh('issue', 'comment', str(issues[0]['number']), '--repo', repo, '--body', body)
    else:
        gh('issue', 'create', '--repo', repo, '--title', 'KSeF upstream update detected',
           '--label', 'ksef-update', '--body', body)

    # Notification failure raises before this write, so a later run retries.
    STATE.write_text(json.dumps({'last_notified': current}, indent=2) + '\n')


if __name__ == '__main__':
    main()
