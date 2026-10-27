import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
const load = name => JSON.parse(readFileSync(new URL(`../${name}.json`, import.meta.url)));
const config = { alertEmailFrom: 'alerts@example.com', alertEmailTo: 'owner@example.com', n8nBaseUrl: 'https://n8n.example.com/' };
const now = '2026-01-01T00:00:00.000Z';
async function run(d, json, configuration = config) {
 const node = d.nodes.find(n => n.name === 'Build Alert');
 const fn = new AsyncFunction('$input', '$', 'Date', node.parameters.jsCode);
 class FixedDate extends Date { constructor(...args) { super(...(args.length ? args : [now])); } }
 return (await fn({ first: () => ({ json }) }, name => {
  assert.equal(name, 'Configuration (EDIT ME)');
  return { first: () => ({ json: configuration }) };
 }, FixedDate))[0].json;
}
const payload = {
 workflow: { id: '7', name: 'Receive invoices' },
 execution: { id: '42', url: 'https://n8n.example.com/execution/42', lastNodeExecuted: 'Poll Gateway',
  error: { name: 'NodeApiError', message: 'Gateway unavailable', stack: 'PRIVATE_STACK', context: { apiKey: 'PRIVATE_CONTEXT' } },
  data: { input: 'PRIVATE_INPUT', output: 'PRIVATE_OUTPUT' }, headers: { 'X-Api-Key': 'PRIVATE_HEADER' } },
};
for (const file of ['error-alert', 'error-alert-PL']) {
 const d = load(file);
 test(`${file}: trigger/configuration/build/email graph and SMTP configuration`, () => {
  assert.equal(d.nodes.find(n => n.name === 'Error Trigger').type, 'n8n-nodes-base.errorTrigger');
  for (const [source, target] of [['Error Trigger', 'Configuration (EDIT ME)'], ['Configuration (EDIT ME)', 'Build Alert'], ['Build Alert', 'Send Email']]) {
   assert.deepEqual(d.connections[source].main, [[{ node: target, type: 'main', index: 0 }]]);
  }
  const set = d.nodes.find(n => n.name === 'Configuration (EDIT ME)');
  assert.equal(set.parameters.includeOtherFields, true, 'Set preserves trigger payload');
  const assignments = Object.fromEntries(set.parameters.assignments.assignments.map(a => [a.name, a.value]));
  assert.deepEqual(assignments, { alertEmailFrom: 'alerts@example.com', alertEmailTo: 'owner@example.com', n8nBaseUrl: '' });
  const email = d.nodes.find(n => n.name === 'Send Email');
  assert.equal(email.type, 'n8n-nodes-base.emailSend');
  assert.deepEqual(email.credentials, {});
  assert.equal(email.parameters.emailFormat, 'text');
  assert.deepEqual(email.parameters.options, { appendAttribution: false });
  for (const [key, value] of Object.entries({ fromEmail: 'fromEmail', toEmail: 'toEmail', subject: 'subject', text: 'text' })) {
   assert.equal(email.parameters[key], `={{ $json.${value} }}`);
  }
  for (const key of ['id', 'name']) assert.equal(new Set(d.nodes.map(n => n[key])).size, d.nodes.length);
  assert.equal(d.connections['Send Email'], undefined);
 });
 test(`${file}: full error becomes a minimal plain-text email`, async () => {
  const result = await run(d, payload);
  assert.deepEqual(result, {
   fromEmail: config.alertEmailFrom, toEmail: config.alertEmailTo,
   subject: '[n8n error] Receive invoices',
   text: `Workflow: Receive invoices\nNode: Poll Gateway\nError: NodeApiError: Gateway unavailable\nTime (UTC): ${now}\nExecution ID: 42\nExecution: https://n8n.example.com/execution/42`,
  });
  assert.doesNotMatch(JSON.stringify(result), /PRIVATE_|stack|context|headers/);
 });
 test(`${file}: trigger error and missing/malformed optional fields have useful fallbacks`, async () => {
  const result = await run(d, { workflow: { name: 'Scheduled job' }, trigger: { error: {
   name: 'WorkflowActivationError', message: 'Trigger failed', node: { name: 'Schedule', parameters: { secret: 'PRIVATE' } }, timestamp: 1654609328787,
  } } });
  assert.match(result.text, /Node: Schedule\nError: WorkflowActivationError: Trigger failed/);
  assert.doesNotMatch(result.text, /Execution|PRIVATE/);
  for (const input of [{}, null, { workflow: null, execution: null }, { execution: { error: { message: { token: 'PRIVATE' } } } }]) {
   const fallback = await run(d, input);
   assert.match(fallback.text, /Workflow: Unknown workflow\nNode: Unknown node\nError: Unknown error/);
   assert.doesNotMatch(fallback.text, /undefined|null|object Object|PRIVATE/);
  }
  const cause = await run(d, { trigger: { error: { cause: { message: 'Activation cause' } } } });
  assert.match(cause.text, /Activation cause/);
 });
 test(`${file}: message is bounded and credentials are redacted before truncation`, async () => {
  const long = await run(d, { execution: { error: { message: 'a'.repeat(2000) } } });
  assert.equal(long.text.split('\n').find(l => l.startsWith('Error: ')).slice(7).length, 500);
  for (const message of [
   'Authorization: Bearer abc.DEF_123-456==; X-Api-Key: super-secret-key',
   'bearer abcdef\nx-api-key: "quoted-secret"',
   'X-Api-Key: secret-start-' + 'z'.repeat(2000),
   'X-Api-Key=another-secret, failure',
  ]) {
   const result = await run(d, { execution: { error: { message } } });
   assert.match(result.text, /\[REDACTED\]/);
   assert.doesNotMatch(result.text, /abc\.DEF|super-secret|abcdef|quoted-secret|secret-start|another-secret|zzzz/);
  }
 });
 test(`${file}: base URL controls links, ID fallback and unsafe URLs`, async () => {
  assert.doesNotMatch((await run(d, payload, { ...config, n8nBaseUrl: '' })).text, /https?:\/\//);
  const noUrl = structuredClone(payload); delete noUrl.execution.url;
  assert.match((await run(d, noUrl)).text, /Execution: https:\/\/n8n.example.com\/executions\/42$/);
  for (const url of ['javascript:alert(1)', config.n8nBaseUrl.replace('://', '://user:placeholder@') + 'execution/42', 'https://n8n.example.com/execution/42?token=PRIVATE', 'https://evil.example/execution/42']) {
   assert.match((await run(d, { ...payload, execution: { ...payload.execution, url } })).text, /Execution: https:\/\/n8n.example.com\/executions\/42$/);
  }
  assert.doesNotMatch((await run(d, payload, { ...config, n8nBaseUrl: 'invalid' })).text, /https?:\/\//);
 });
 test(`${file}: export has no stored credentials or pinned execution data`, () => {
  for (const n of d.nodes) assert.deepEqual(n.credentials || {}, {});
  assert.deepEqual(d.pinData || {}, {});
  assert.equal(d.staticData, undefined);
  for (const key of ['saveDataSuccessExecution', 'saveDataErrorExecution']) assert.equal(d.settings[key], 'none');
  for (const key of ['saveManualExecutions', 'saveExecutionProgress']) assert.equal(d.settings[key], false);
  assert.doesNotMatch(JSON.stringify(d), /PRIVATE_|credentialId|apiKey"|password"/);
 });
}
test('error-alert: EN/PL identical except sticky-note content', () => {
 const normalize = d => ({ ...d, nodes: d.nodes.map(n => n.type.endsWith('.stickyNote') ? { ...n, parameters: { ...n.parameters, content: '' } } : n) });
 assert.deepEqual(normalize(load('error-alert')), normalize(load('error-alert-PL')));
 assert.notEqual(load('error-alert').nodes[0].parameters.content, load('error-alert-PL').nodes[0].parameters.content);
});
