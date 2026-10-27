import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const load = name => JSON.parse(readFileSync(new URL(`../${name}.json`, import.meta.url)));
const files = ['receive-invoices', 'receive-invoices-PL'];
const node = (workflow, name) => {
  const found = workflow.nodes.find(n => n.name === name);
  assert.ok(found, `Missing node: ${name}`);
  return found;
};
const targets = (workflow, name) => workflow.connections[name]?.main?.flat().map(edge => edge.node) ?? [];
const evaluate = (expression, invoice, config) => {
  assert.ok(expression.startsWith('={{') && expression.endsWith('}}'));
  // Resolve current-item metadata, not the first invoice of the batch.
  const lookup = name => name === 'Configuration (EDIT ME)'
    ? { first: () => ({ json: config }) }
    : (assert.equal(name, 'Split Invoices'), { item: { json: invoice } });
  return new Function('$', `return (${expression.slice(3, -2)});`)(lookup);
};

for (const file of files) {
  const workflow = load(file);
  test(`${file}: real SMTP node replaces TODO`, () => {
    const mail = node(workflow, 'Send Email');
    assert.equal(mail.type, 'n8n-nodes-base.emailSend');
    // n8n 1.100.1 EmailSend.node.ts registers 2.1 as its default version.
    assert.equal(mail.typeVersion, 2.1);
    assert.equal(mail.parameters.resource, 'email');
    assert.equal(mail.parameters.operation, 'send');
    assert.equal(workflow.nodes.some(n => n.name === 'Notify (TODO)'), false);
  });
  test(`${file}: save -> mail -> checkpoint is a fail-stop delivery path`, () => {
    assert.deepEqual(targets(workflow, 'Save PDF To Disk'), ['Send Email']);
    assert.deepEqual(targets(workflow, 'Send Email'), ['Commit Checkpoint']);
    for (const name of ['Download Invoice PDF', 'Save PDF To Disk', 'Send Email']) {
      const step = node(workflow, name);
      assert.notEqual(step.continueOnFail, true);
      assert.ok(step.onError === undefined || step.onError === 'stopWorkflow');
      assert.notEqual(step.executeOnce, true);
      assert.notEqual(step.disabled, true);
    }
    assert.notEqual(node(workflow, 'Send Email').retryOnFail, true);
    const predecessors = Object.keys(workflow.connections).filter(name => targets(workflow, name).includes('Commit Checkpoint'));
    assert.deepEqual(predecessors, ['Send Email']);
  });
  test(`${file}: attachment uses the binary field downloaded and preserved by disk write`, () => {
    const download = node(workflow, 'Download Invoice PDF').parameters.options.response.response;
    const save = node(workflow, 'Save PDF To Disk').parameters;
    const mail = node(workflow, 'Send Email').parameters;
    assert.equal(download.responseFormat, 'file');
    assert.equal(save.operation, 'write');
    assert.equal(download.outputPropertyName, 'data');
    assert.equal(save.dataPropertyName, download.outputPropertyName);
    assert.equal(mail.options.attachments, save.dataPropertyName);
  });
  test(`${file}: public JSON contains address placeholders and no credentials or secrets`, () => {
    const config = node(workflow, 'Configuration (EDIT ME)').parameters.assignments.assignments;
    for (const name of ['notifyEmailFrom', 'notifyEmailTo']) {
      assert.equal(config.find(a => a.name === name)?.value, 'faktury@example.com');
    }
    assert.deepEqual(node(workflow, 'Send Email').credentials, {});
    for (const step of workflow.nodes) {
      assert.deepEqual(step.credentials ?? {}, {}, `${step.name}: no exported credential IDs`);
      const parameters = JSON.stringify(step.parameters);
      assert.doesNotMatch(parameters, /"(?:password|smtpPassword|apiKey|gatewayApiKey|accessToken)"\s*:/i);
    }
    assert.match(workflow.nodes.filter(n => n.type.endsWith('.stickyNote')).map(n => n.parameters.content).join('\n'), /notifyEmailFrom[\s\S]*notifyEmailTo/);
  });
  test(`${file}: each invoice gets its own plain-text metadata and configured addresses`, () => {
    const mail = node(workflow, 'Send Email').parameters;
    assert.equal(mail.emailFormat, 'text');
    const config = { notifyEmailFrom: 'from@example.com', notifyEmailTo: 'to@example.com' };
    assert.equal(evaluate(mail.fromEmail, {}, config), config.notifyEmailFrom);
    assert.equal(evaluate(mail.toEmail, {}, config), config.notifyEmailTo);
    for (const invoice of [
      { ksefNumber: 'NIP-20260101-AAA', sellerName: 'Seller A', sellerNip: '1234567890', issueDate: '2026-01-01T00:00:00+00:00', grossAmount: 123.45, currency: 'PLN' },
      { ksefNumber: 'NIP-20260102-BBB', sellerName: 'Seller B', sellerNip: '0987654321', issueDate: '2026-01-02T00:00:00+00:00', grossAmount: 0, currency: 'EUR' },
      { ksefNumber: 'NIP-20260103-CCC', sellerName: '', sellerNip: '1111111111', issueDate: '2026-01-03T00:00:00+00:00', grossAmount: -12.5, currency: 'PLN' },
    ]) {
      const subject = evaluate(mail.subject, invoice, config);
      const text = evaluate(mail.text, invoice, config);
      for (const value of [invoice.ksefNumber, invoice.sellerName || invoice.sellerNip, String(invoice.grossAmount), invoice.currency]) {
        assert.ok(subject.includes(value), `Subject missing ${value}: ${subject}`);
        assert.ok(text.includes(value), `Body missing ${value}: ${text}`);
      }
      assert.ok(text.includes(invoice.issueDate.slice(0, 10)));
      assert.doesNotMatch(subject + text, /undefined|null/);
    }
  });
}

test('receive-invoices: EN/PL workflows match outside sticky notes', () => {
  const operational = workflow => ({ ...workflow, nodes: workflow.nodes.filter(n => n.type !== 'n8n-nodes-base.stickyNote') });
  assert.deepEqual(operational(load(files[0])), operational(load(files[1])));
});
