import copy
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import ksef_update_check as monitor


class UpdateMonitorTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.state = Path(self.tmp.name) / 'versions.json'
        self.previous = {'sdk_sha': 'a' * 40, 'docs_sha': 'b' * 40,
                         'pdf_sha': 'c' * 40, 'sdk_version': '2.9.0'}
        self.state.write_text(json.dumps({'last_notified': self.previous}))
        self.original = self.state.read_bytes()
        self.upstream = copy.copy(self.previous)
        self.issues = [{'number': 12}]
        self.calls = []
        self.failure = None
        self.docs_response = None
        for patcher in (patch.object(monitor, 'STATE', self.state),
                        patch.object(monitor, 'gh', side_effect=self.gh),
                        patch.dict(os.environ, {'GITHUB_REPOSITORY': 'owner/gateway'})):
            patcher.start()
            self.addCleanup(patcher.stop)

    def gh(self, *args):
        self.calls.append(args)
        if self.failure and self.failure in args:
            raise subprocess.CalledProcessError(1, ['gh', *args])
        if args[0] == 'api':
            if args[1].endswith('/releases/latest'):
                return json.dumps({'tag_name': self.upstream['sdk_version']})
            for name, repo in monitor.REPOS.items():
                if repo in args[1]:
                    if name == 'docs' and self.docs_response is not None:
                        return self.docs_response
                    return json.dumps([{'sha': self.upstream[f'{name}_sha']}])
            self.fail(f'Unexpected API endpoint: {args[1]}')
        if args[1] == 'list':
            return json.dumps(self.issues)
        return 'https://github.com/owner/gateway/issues/12'

    def notifications(self):
        return [call for call in self.calls if call[:2] in
                [('issue', 'comment'), ('issue', 'create')]]

    def test_empty_docs_sha_preserves_good_state(self):
        self.upstream['docs_sha'] = ''
        with self.assertRaisesRegex(ValueError, 'Invalid docs SHA'):
            monitor.main()
        self.assertEqual(self.state.read_bytes(), self.original)
        self.assertEqual(self.notifications(), [])

    def test_open_stale_issue_receives_new_change(self):
        self.upstream['docs_sha'] = 'd' * 40
        monitor.main()
        self.assertEqual(self.notifications()[0][:3], ('issue', 'comment', '12'))
        self.assertIn('d' * 40, self.notifications()[0][-1])
        self.assertEqual(json.loads(self.state.read_text())['last_notified'], self.upstream)
        monitor.main()
        self.assertEqual(len(self.notifications()), 1)

    def test_closed_issue_does_not_block_new_issue(self):
        self.issues = []  # gh issue list filters to open issues only.
        self.upstream['pdf_sha'] = 'd' * 40
        monitor.main()
        self.assertEqual(self.notifications()[0][:2], ('issue', 'create'))
        self.assertIn('--state', self.calls[-2])
        self.assertIn('open', self.calls[-2])

    def test_unchanged_snapshot_does_not_write_or_notify(self):
        monitor.main()
        self.assertEqual(self.notifications(), [])
        self.assertEqual(self.state.read_bytes(), self.original)
        self.assertFalse(any(call[0] == 'issue' for call in self.calls))

    def test_each_source_and_release_independently_triggers(self):
        for key in self.previous:
            with self.subTest(key=key):
                self.state.write_bytes(self.original)
                self.calls.clear()
                self.upstream = {**self.previous, key: 'd' * 40 if key.endswith('_sha') else '3.0.0'}
                monitor.main()
                self.assertEqual(len(self.notifications()), 1)
                self.assertIn(self.upstream[key], self.notifications()[0][-1])

    def test_invalid_sha_or_redirect_response_fails_before_write(self):
        for value in ('', 'abc1234', 'g' * 40, 'a' * 39, 'a' * 41, None):
            with self.subTest(sha=value):
                self.upstream['docs_sha'] = value
                with self.assertRaises(ValueError):
                    monitor.main()
                self.assertEqual(self.state.read_bytes(), self.original)
        for response in ('[]', '{"message":"Moved Permanently"}', 'not json'):
            with self.subTest(response=response):
                self.docs_response = response
                with self.assertRaises(ValueError):
                    monitor.main()
                self.assertEqual(self.state.read_bytes(), self.original)
        self.assertEqual(self.notifications(), [])

    def test_missing_release_fails_before_write(self):
        for version in ('', ' ', None):
            self.upstream['sdk_version'] = version
            with self.assertRaisesRegex(ValueError, 'Missing SDK release'):
                monitor.main()
        self.assertEqual(self.state.read_bytes(), self.original)
        self.assertEqual(self.notifications(), [])

    def test_api_or_notification_failure_does_not_advance_state(self):
        self.upstream['sdk_sha'] = 'd' * 40
        for operation in ('api', 'list', 'comment', 'create'):
            with self.subTest(operation=operation):
                self.failure = operation
                self.issues = [] if operation == 'create' else [{'number': 12}]
                with self.assertRaises(subprocess.CalledProcessError):
                    monitor.main()
                self.assertEqual(self.state.read_bytes(), self.original)

    def test_untrusted_release_is_literal_data(self):
        self.upstream['sdk_version'] = '$(touch SHOULD_NOT_EXIST)\n"; echo injected; #'
        monitor.main()
        self.assertIn(self.upstream['sdk_version'], self.notifications()[0][-1])
        self.assertEqual(json.loads(self.state.read_text())['last_notified'], self.upstream)

    def test_legacy_snapshot_prompts_full_sha_and_pdf_notification(self):
        self.state.write_text(json.dumps({'last_notified': {
            'sdk_sha': 'a66cbc6', 'docs_sha': '', 'sdk_version': '2.9.0'}}))
        monitor.main()
        self.assertEqual(len(self.notifications()), 1)
        self.assertIn('CIRFMF/ksef-pdf-generator', self.notifications()[0][-1])
        self.assertEqual(json.loads(self.state.read_text())['last_notified'], self.upstream)

    def test_gh_passes_arguments_without_shell(self):
        with patch.object(subprocess, 'check_output', return_value='ok\n') as run:
            self.assertEqual(self.gh_real('issue', '--body', '$(echo injected)'), 'ok')
        run.assert_called_once_with(['gh', 'issue', '--body', '$(echo injected)'], text=True)

    # Keep the real CLI wrapper while other tests replace it with deterministic API data.
    gh_real = staticmethod(monitor.gh)


if __name__ == '__main__':
    unittest.main()
