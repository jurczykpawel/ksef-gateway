import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHmac } from 'node:crypto';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
const files = ['sellf-ksef', 'sellf-ksef-PL', 'woocommerce-ksef', 'woocommerce-ksef-PL'];
const load = f => JSON.parse(readFileSync(new URL(`../${f}.json`, import.meta.url)));
const config = {webhookSecret:'secret', defaultVatRate:23, sellerNip:'123', sellerCity:'Warszawa'};
const woo = {id:123, number:'custom', currency:'PLN', meta_data:[{key:'billing_nip',value:'123'}], billing:{}, line_items:[{name:'Discounted item',quantity:3,subtotal:'15.00',total:'10.00',total_tax:'2.30'}], shipping_lines:[{method_title:'Shipping',total:'5.00',total_tax:'1.15'}], fee_lines:[{name:'Fee',total:'2.00',total_tax:'0.46'}]};
async function run(d,name,json,{sd={},raw,refs={},env={},runIndex=0}={}) {
 const n=d.nodes.find(n=>n.name===name);
 assert.ok(n, `node ${name} exists`);
 const lookup = name => ({first:()=>({json:name==='Configuration (EDIT ME)'?config:refs[name]}),item:{json:refs[name]}});
 const fn = new AsyncFunction('$input','$json','$getWorkflowStaticData','$','$env','require','Buffer','Date','$runIndex',n.parameters.jsCode);
 class FixedDate extends Date { constructor(...args){super(...(args.length?args:['2026-01-01T00:00:00Z']));} static now(){return 1767225600000;} }
 return (await fn.call({helpers:{getBinaryDataBuffer:async()=>{if(!raw) throw Error('missing raw');return raw;}}},{first:()=>({json,binary:raw?{data:{data:raw.toString('base64')}}:undefined})},json,scope=>{assert.equal(scope,'global');return sd;},lookup,{SELLF_WEBHOOK_SECRET:'secret',WOO_WEBHOOK_SECRET:'secret',...env},require,Buffer,FixedDate,runIndex))[0]?.json;
}
for(const f of files){
 const d=load(f), sellf=f.startsWith('sellf');
 test(`${f}: malformed signatures deny without exception`,async()=>{
  const headers=sellf?{'x-sellf-signature':`t=1767225600,v1=${'z'.repeat(64)}`}:{'x-wc-webhook-signature':'!'.repeat(44)};
  assert.equal((await run(d,'Verify Signature',{headers},{raw:Buffer.from('{}')})).valid,false);
  assert.equal(d.nodes.find(n=>n.name==='Respond Unauthorized').parameters.options.responseCode,401);
 });
 test(`${f}: exact binary helper required, valid and tampered signatures`,async()=>{
  const raw=Buffer.from('{ "test": true }'); const t='1767225600';
  const sig=createHmac('sha256','secret').update(sellf?Buffer.concat([Buffer.from(t+'.'),raw]):raw).digest(sellf?'hex':'base64');
  const headers=sellf?{'x-sellf-signature':`t=${t},v1=${sig}`}:{'x-wc-webhook-signature':sig};
  assert.equal((await run(d,'Verify Signature',{headers,body:{test:true}},{raw})).valid,true);
  assert.equal((await run(d,'Verify Signature',{headers,body:{test:true}})).valid,false);
  assert.equal((await run(d,'Verify Signature',{headers},{raw:Buffer.from('{}')})).valid,false);
 });
 test(`${f}: gateway key outside Set and send has no automatic retry`,()=>{
  const assignments=d.nodes.find(n=>n.type.endsWith('.set')).parameters.assignments.assignments;
  assert.ok(!assignments.some(a=>['gatewayApiKey','webhookSecret'].includes(a.name)));
  const send=d.nodes.find(n=>n.name==='Send to KSeF Gateway');
  assert.equal(send.parameters.genericAuthType,'httpHeaderAuth');assert.notEqual(send.retryOnFail,true);
 });
 test(`${f}: duplicate delivery never reaches a second POST`,async()=>{
  // Execute the state gate twice against the same persisted workflow data.
  const sd={};let posts=0;
  // Old workflow has no gate: its existing payload goes straight to the POST.
  for(let i=0;i<2;i++){
   const payload={invoice:{invoiceNumber:'ORDER/123'},eventKey:'order:123'};
   const gated=d.nodes.some(n=>n.name==='Reserve Invoice')?await run(d,'Reserve Invoice',payload,{sd}):payload;
   if(!gated.duplicate) posts++;
  }
  assert.equal(posts,1);
 });
 test(`${f}: amounts and full identifiers`,async()=>{
  if(sellf){
   const p=await run(d,'Build KSeF Payload',{sessionId:'cs_full_1234',amount:123,productName:'Product',invoiceNip:'123'});
   const invoice=p.invoice || p;
   assert.equal(invoice.items[0].unitPrice,100);assert.ok(invoice.invoiceNumber.includes('cs_full_1234'));
  }else{
   const p=await run(d,'Transform to KSeF',{body:woo});
   assert.equal(Math.round(p.invoice.items[0].unitPrice*p.invoice.items[0].quantity*100),1000);
   assert.equal(p.invoice.items.length,3);
   assert.equal(p.invoice.items.reduce((s,i)=>s+Math.round(i.unitPrice*i.quantity*100),0),1700);
   assert.equal(p.invoice.invoiceNumber,'WOO/123');
  }
 });
 test(`${f}: JSON graph references and IDs are unique`,()=>{
  for(const key of ['id','name']) assert.equal(new Set(d.nodes.map(n=>n[key])).size,d.nodes.length);
  for(const [source,ports] of Object.entries(d.connections)){
   assert.ok(d.nodes.some(n=>n.name===source));
   for(const edge of ports.main.flat()) assert.ok(d.nodes.some(n=>n.name===edge.node),edge.node);
  }
 });
}

for (const f of files) {
 const d=load(f);
 test(`${f}: bounded safe retries, ambiguous responses never retry`, async()=>{
  for(const [response,expected,retry] of [
   [{statusCode:200,body:{success:true,data:{ksefNumber:'KSEF-123'}}},'sent',false],
   [{statusCode:429,headers:{'retry-after':'7'},body:{}},'in_progress',true],
   [{statusCode:429,headers:{'retry-after':'Thu, 01 Jan 2026 00:00:09 GMT'},body:{}},'in_progress',true],
   [{statusCode:503,body:'<!doctype html><html>Proxy unavailable</html>'},'in_progress',true],
   [{statusCode:503,body:{error:'KSeF circuit open'}},'uncertain',false],
   [{statusCode:429,body:{}},'uncertain',false],
   [{statusCode:429,headers:{'retry-after':'301'}},'uncertain',false],
   [{statusCode:502,body:{}},'uncertain',false],
   [{error:'socket timeout'},'uncertain',false],
   [{statusCode:200,body:{}},'uncertain',false],
  ]) {
   const sd={invoices:[{key:'order:123',status:'in_progress'}]};
   const result=await run(d,'Record Send Result',response,{sd,refs:{'Reserve Invoice':{eventKey:'order:123'}}});
   assert.equal(!!result.retry,retry); assert.equal(sd.invoices[0].status,expected);
   if(retry) assert.ok(result.delay>=1 && result.delay<=300);
  }
  const sd={invoices:[{key:'order:123',status:'in_progress'}]};
  const result=await run(d,'Record Send Result',{statusCode:429,headers:{'retry-after':'1'}},{sd,refs:{'Reserve Invoice':{eventKey:'order:123'}},runIndex:2});
  assert.equal(result.status,'not_accepted');assert.equal(!!result.retry,false);
 });
 test(`${f}: completed/uncertain IDs persist across reload; capacity preserves unresolved sends`,async()=>{
  for(const status of ['sent','uncertain','in_progress','not_accepted']) {
   const sd=JSON.parse(JSON.stringify({invoices:[{key:'order:123',status}]}));
   const p=await run(d,'Reserve Invoice',{eventKey:'order:123'},{sd});
   assert.equal(p.duplicate,true);assert.equal(p.status,status);assert.equal(p.send,undefined);
  }
  const sd={invoices:Array.from({length:1000},(_,i)=>({key:String(i),status:'uncertain'}))};
  const p=await run(d,'Reserve Invoice',{eventKey:'new'},{sd});
  assert.match(p.error,/capacity/);assert.equal(sd.invoices.length,1000);
  sd.invoices[50].status='sent';
  assert.equal((await run(d,'Reserve Invoice',{eventKey:'new'},{sd})).send,true);
  assert.equal(sd.invoices.length,1000);assert.ok(sd.invoices.some(e=>e.key==='new'));
 });
 test(`${f}: health loop stops after budget and cannot send when unhealthy`,async()=>{
  assert.deepEqual(await run(d,'Check Health',{status:'ok'}),{ready:true,again:false});
  assert.deepEqual(await run(d,'Check Health',{}, {runIndex:0}),{ready:false,again:true});
  assert.deepEqual(await run(d,'Check Health',{}, {runIndex:8}),{ready:false,again:false});
  assert.equal(d.connections['Gateway Ready?'].main[0][0].node,'Send to KSeF Gateway');
  assert.equal(d.connections['Gateway Ready?'].main[1][0].node,'Wake Again?');
  assert.equal(d.connections['Wake Again?'].main[1][0].node,'Wake Failed');
 });
 test(`${f}: no early acknowledgement; duplicate branch bypasses HTTP`,()=>{
  assert.equal(d.connections['Send Invoice?'].main[1][0].node,'Respond OK');
  assert.equal(d.connections['Reserve Invoice'].main[0][0].node,'Send Invoice?');
  assert.equal(d.connections['Signature Valid?'].main[0][0].node,f.startsWith('sellf')?'Is Purchase?':'Transform to KSeF');
  assert.equal(d.connections['Respond OK'],undefined);
 });
}
for(const prefix of ['sellf','woocommerce']) test(`${prefix}: PL/EN logic parity`,()=>{
 const logic=d=>({settings:d.settings,connections:d.connections,nodes:d.nodes.filter(n=>!n.type.endsWith('.stickyNote'))});
 assert.deepEqual(logic(load(`${prefix}-ksef`)),logic(load(`${prefix}-ksef-PL`)));
});

for(const f of files) {
 const d=load(f),sellf=f.startsWith('sellf');
 test(`${f}: no raw bytes, missing secret, malformed JSON and stale timestamps deny`,async()=>{
  const signature=raw=>{
   const hash=createHmac('sha256','secret').update(sellf?Buffer.concat([Buffer.from('1767225600.'),raw]):raw).digest(sellf?'hex':'base64');
   return sellf?{'x-sellf-signature':`t=1767225600,v1=${hash}`}:{'x-wc-webhook-signature':hash};
  };
  const raw=Buffer.from('{}');
  assert.equal((await run(d,'Verify Signature',{headers:signature(raw),body:{}})).valid,false);
  assert.equal((await run(d,'Verify Signature',{headers:signature(raw)},{raw,env:{[sellf?'SELLF_WEBHOOK_SECRET':'WOO_WEBHOOK_SECRET']:''}})).valid,false);
  const invalid=Buffer.from('{');
  assert.equal((await run(d,'Verify Signature',{headers:signature(invalid)},{raw:invalid})).valid,false);
  if(sellf) assert.equal((await run(d,'Verify Signature',{headers:{'x-sellf-signature':`t=0,v1=${'a'.repeat(64)}`}},{raw})).valid,false);
 });
 test(`${f}: execute send graph twice, with mocked HTTP and persisted state`,async()=>{
  const sd={};let posts=0;
  async function execute(){
   let name='Reserve Invoice',json={eventKey:'order:123',invoice:{invoiceNumber:'ORDER/123'}};
   const refs={},visits={};
   for(let step=0;step<50;step++) {
    const node=d.nodes.find(n=>n.name===name);assert.ok(node,name);
    const runIndex=visits[name]||0;visits[name]=runIndex+1;
    let output=0;
    if(node.type.endsWith('.code')) json=await run(d,name,json,{sd,refs,runIndex});
    else if(node.type.endsWith('.if')) {
     const expr=node.parameters.conditions.conditions[0].leftValue.slice(3,-2).trim();
     output=new Function('$json',`return (${expr})`)(json)?0:1;
    } else if(name==='Wake Gateway') json={status:'ok'};
    else if(name==='Send to KSeF Gateway') {posts++;json={statusCode:200,body:{success:true,data:{ksefNumber:'KSEF-123'}}};}
    else if(node.type.endsWith('.respondToWebhook')) return json;
    else assert.fail(`Unexpected node ${name}`);
    refs[name]=json;
    const next=d.connections[name]?.main[output]?.[0]?.node;
    assert.ok(next,`${name} has next node`);name=next;
   }
   assert.fail('graph did not terminate');
  }
  assert.equal((await execute()).ksefNumber,'KSEF-123');
  // Simulate persistence/reload, retaining the same mutable scope object.
  Object.assign(sd,JSON.parse(JSON.stringify(sd)));
  assert.equal((await execute()).duplicate,true);assert.equal(posts,1);
 });
}
for(const f of ['sellf-ksef','sellf-ksef-PL']) test(`${f}: skipped webhook reaches a response output`,()=>{
 const d=load(f);
 for(const name of ['Is Purchase?','Needs Invoice?']) {
  const node=d.nodes.find(n=>n.name===name);
  assert.equal(node.parameters.options.fallbackOutput,'extra');
  assert.equal(d.connections[name].main[1][0].node,'Respond Skipped');
 }
});
