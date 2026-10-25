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
async function run(d,name,json,{sd={},raw,refs={},env={},configuration=config,logs=[],runIndex=0}={}) {
 const n=d.nodes.find(n=>n.name===name);
 assert.ok(n, `node ${name} exists`);
 const lookup = name => ({first:()=>({json:name==='Configuration (EDIT ME)'?configuration:refs[name]}),item:{json:refs[name]}});
 const fn = new AsyncFunction('$input','$json','$getWorkflowStaticData','$','$env','require','Buffer','Date','$runIndex','console',n.parameters.jsCode);
 class FixedDate extends Date { constructor(...args){super(...(args.length?args:['2026-01-01T00:00:00Z']));} static now(){return 1767225600000;} }
 return (await fn.call({helpers:{getBinaryDataBuffer:async()=>{if(!raw) throw Error('missing raw');return raw;}}},{first:()=>({json,binary:raw?{data:{data:raw.toString('base64')}}:undefined})},json,scope=>{assert.equal(scope,'global');return sd;},lookup,env===null?new Proxy({}, {get(){throw Error('env access blocked');}}):{SELLF_WEBHOOK_SECRET:'secret',WOO_WEBHOOK_SECRET:'secret',...env},require,Buffer,FixedDate,runIndex,{warn:message=>logs.push(message)}))[0]?.json;
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
  assert.ok(!assignments.some(a=>a.name==='gatewayApiKey'));
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
   const p=await run(d,'Build KSeF Payload',{sessionId:'cs_full_1234',amount:123,productName:'Product',invoiceNip:'5260250995'});
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
  assert.equal((await run(d,'Verify Signature',{headers:signature(raw)},{raw,configuration:{...config,webhookSecret:''},env:{[sellf?'SELLF_WEBHOOK_SECRET':'WOO_WEBHOOK_SECRET']:''}})).valid,false);
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

for(const f of files) {
 const d=load(f),sellf=f.startsWith('sellf'),envName=sellf?'SELLF_WEBHOOK_SECRET':'WOO_WEBHOOK_SECRET';
 const signed=(secret,body={})=>{
  const raw=Buffer.from(JSON.stringify(body));
  const sig=createHmac('sha256',secret).update(sellf?Buffer.concat([Buffer.from('1767225600.'),raw]):raw).digest(sellf?'hex':'base64');
  return {raw,headers:sellf?{'x-sellf-signature':`t=1767225600,v1=${sig}`}:{'x-wc-webhook-signature':sig}};
 };
 for(const [mode,env] of [['blocked',null],['undefined',{[envName]:undefined}],['empty',{[envName]:''}]]) {
  test(`${f}: ${mode} env falls back to Configuration`,async()=>{
   const {raw,headers}=signed('configuration-secret');
   const result=await run(d,'Verify Signature',{headers},{raw,env,configuration:{...config,webhookSecret:'configuration-secret'}});
   assert.equal(result.valid,true);
   assert.deepEqual(result,{valid:true,body:{}});
  });
 }
 test(`${f}: env takes precedence over Configuration`,async()=>{
  for(const [secret,expected] of [['env-secret',true],['configuration-secret',false]]) {
   const {raw,headers}=signed(secret);
   assert.equal((await run(d,'Verify Signature',{headers},{raw,env:{[envName]:'env-secret'},configuration:{...config,webhookSecret:'configuration-secret'}})).valid,expected);
  }
 });
 test(`${f}: empty, missing and placeholder secrets deny and log without exposing values`,async()=>{
  for(const secret of ['',undefined,'   ','your-webhook-secret']) {
   for(const env of [null,{[envName]:secret}]) {
    const logs=[],{raw,headers}=signed(secret||'unused');
    assert.equal((await run(d,'Verify Signature',{headers},{raw,env,configuration:{...config,webhookSecret:secret},logs})).valid,false);
    assert.match(logs.join('\n'),/webhook secret.*missing|webhook secret.*placeholder/i);
    if(secret?.trim()) assert.ok(!logs.join('\n').includes(secret));
   }
  }
 });
 test(`${f}: execution saving disabled and Configuration fallback is explicit`,()=>{
  for(const key of ['saveDataSuccessExecution','saveDataErrorExecution']) assert.equal(d.settings[key],'none');
  for(const key of ['saveManualExecutions','saveExecutionProgress']) assert.equal(d.settings[key],false);
  const a=d.nodes.find(n=>n.name==='Configuration (EDIT ME)').parameters.assignments.assignments;
  assert.equal(a.find(a=>a.name==='webhookSecret')?.value,'your-webhook-secret');
  const notes=d.nodes.filter(n=>n.type.endsWith('.stickyNote')).map(n=>n.parameters.content).join('\n');
  assert.match(notes,/N8N_BLOCK_ENV_ACCESS_IN_NODE=false/);
  assert.match(notes,/webhookSecret/);
  assert.match(notes,f.endsWith('-PL')?/nie udostępniaj eksportu/i:/do not share workflow exports/i);
 });
 test(`${f}: graph after Configuration keeps secrets out of outputs, webhook and gateway request`,async()=>{
  // Configuration output intentionally contains the fallback secret (coordinator decision).
  // Exclude only that output; execute actual downstream code and expressions with HTTP stubs.
  for(const env of [null,{[envName]:'env-secret'}]) for(const badSignature of [false,true]) {
   const configuration={...config,ksefGatewayUrl:'https://gateway.example',webhookSecret:'configuration-secret'};
   const body=sellf?{event:'purchase.completed',data:{customer:{email:'test@example.com'},product:{name:'Product'},order:{amount:12300,currency:'pln',sessionId:'session-123'},invoice:{needsInvoice:true,nip:'5260250995'}}}:woo;
   const {raw,headers}=signed(badSignature?'wrong-secret':env?'env-secret':'configuration-secret',body);
   const refs={},outputs=[],requests=[],sd={};let name='Verify Signature',json={...configuration,headers,body},posts=0;
   const lookup=name=>({first:()=>({json:name==='Configuration (EDIT ME)'?configuration:refs[name]})});
   const evaluate=value=>value?.startsWith('={{')?new Function('$json','$',`return (${value.slice(3,-2).trim()})`)(json,lookup):value;
   const condition=c=>{
    const left=evaluate(c.leftValue);
    switch(c.operator.operation) {
     case 'true':return left===true;
     case 'false':return left===false;
     case 'equals':return left===c.rightValue;
     case 'notEquals':return left!==c.rightValue;
     default:assert.fail(c.operator.operation);
    }
   };
   let ended=false;
   for(let step=0;step<50;step++) {
    const node=d.nodes.find(n=>n.name===name);assert.ok(node,name);let port=0;
    if(node.type.endsWith('.code')) json=await run(d,name,json,{sd,refs,raw,env,configuration});
    else if(node.type.endsWith('.if')) port=node.parameters.conditions.conditions.every(condition)?0:1;
    else if(node.type.endsWith('.switch')) port=node.parameters.rules.values[0].conditions.conditions.every(condition)?0:1;
    else if(name==='Wake Gateway') json={status:'ok'};
    else if(name==='Send to KSeF Gateway') {
     requests.push({url:evaluate(node.parameters.url),body:evaluate(node.parameters.jsonBody)});posts++;
     json={statusCode:200,body:{success:true,data:{ksefNumber:'KSEF-123'}}};
    } else if(node.type.endsWith('.respondToWebhook')) {
     const response=JSON.parse(evaluate(node.parameters.responseBody));outputs.push(response);
     assert.equal(node.parameters.options.responseCode,badSignature?401:200);
     if(!badSignature) assert.equal(response.ksefNumber,'KSEF-123');
     ended=true;break;
    } else assert.fail(`Unexpected node ${name}`);
    refs[name]=json;outputs.push(json);
    name=d.connections[name]?.main[port]?.[0]?.node;
   }
   assert.equal(ended,true);assert.equal(posts,badSignature?0:1);
   const serialized=JSON.stringify({outputs,requests,sd});
   for(const secret of ['configuration-secret','env-secret']) assert.ok(!serialized.includes(secret),secret);
  }
 });
}

// Sellf classification: execute the shipped Code nodes, without provider/network access.
const sellfPurchase = {sessionId:'session-123',paymentIntentId:'pi-123',amount:123,
 productName:'Product',email:'buyer@example.com',invoiceCompany:'Buyer',needsInvoice:true,
 invoiceNip:'5260250995',invoiceAddress:'Street 1',invoiceCity:'Warszawa',invoiceCountry:'PL'};
for (const f of ['sellf-ksef','sellf-ksef-PL']) {
 const d=load(f);
 for (const [country,id,field,expected,rate] of [
  ['PL','5260250995','nip','5260250995',23],
  ['PL',' pl 526-025-09-95 ','nip','5260250995',23],
  [undefined,'526-025-09-95','nip','5260250995',23],
  ['DE','de 123-456-789','euVatNumber','DE123456789','np II'],
  ['FR','12345678901','euVatNumber','FR12345678901','np II'],
  ['GR','EL123456789','euVatNumber','EL123456789','np II'],
  ['XI','123456789','euVatNumber','XI123456789','np II'],
  ['US','12-3456789','taxId','12-3456789','np I'],
  ['GB','GB123456789','taxId','GB123456789','np I'],
 ]) test(`${f}: buyer ${country || 'missing country'} / ${id}`,async()=>{
  const result=JSON.parse(JSON.stringify(await run(d,'Build KSeF Payload',{
   ...sellfPurchase,invoiceCountry:country,invoiceNip:id
  })));
  assert.equal(result.invoice.buyer[field],expected);
  assert.equal(['nip','euVatNumber','taxId','noId'].filter(k=>k in result.invoice.buyer).length,1);
  assert.equal(result.invoice.buyer.address.country,country || 'PL');
  assert.equal(result.invoice.items[0].vatRate,rate);
  assert.equal(result.invoice.items[0].unitPrice,rate===23?100:123);
  // Gateway field contract: exactly one identity; EU 1–12 ASCII symbols; tax ID <=50.
  if(field==='nip') assert.match(expected,/^[0-9]{10}$/);
  if(field==='euVatNumber') assert.match(expected,/^[A-Z]{2}[A-Z0-9+*]{1,12}$/);
  if(field==='taxId') assert.ok(expected.trim().length>0 && expected.length<=50);
 });
 test(`${f}: domestic payload remains byte identical`,async()=>{
  const result=await run(d,'Build KSeF Payload',sellfPurchase);
  assert.equal(JSON.stringify(result),'\u007b"eventKey":"sellf:session-123","invoice":{"invoiceNumber":"FV/session-123","issueDate":"2026-01-01","saleDate":"2026-01-01","issuePlace":"Warszawa","currency":"PLN","type":"VAT","seller":{"nip":"123","address":{"city":"Warszawa"}},"buyer":{"nip":"5260250995","name":"Buyer","address":{"street":"Street 1","city":"Warszawa","country":"PL"}},"items":[{"name":"Product","unit":"szt.","quantity":1,"unitPrice":100,"vatRate":23}],"payment":{"paid":true,"date":"2026-01-01","method":"transfer"},"_meta":{"sessionId":"session-123","paymentIntentId":"pi-123","buyerEmail":"buyer@example.com"}}}');
 });
 test(`${f}: unsafe buyer/config skips through actual graph without HTTP or reservation`,async()=>{
  for (const input of [
   {invoiceCountry:'DE',invoiceNip:null}, {invoiceCountry:'FR',invoiceNip:'   '},
   {invoiceNip:'garbage'}, {invoiceNip:'123'}, {invoiceNip:123},
   {invoiceCountry:'DE',invoiceNip:'FR123456789'}, {invoiceCountry:'DE',invoiceNip:'DE'},
   {invoiceCountry:'DE',invoiceNip:'DE!123'}, {invoiceCountry:'DE',invoiceNip:'DE'+'1'.repeat(13)},
   {invoiceCountry:'US',invoiceNip:'!'}, {invoiceCountry:'US',invoiceNip:'X'.repeat(51)},
   {invoiceCountry:'ZZ'}, {invoiceCountry:'US',invoiceNip:null}, {needsInvoice:false},
   {configuration:{...config,defaultVatRate:22}},
   {configuration:{...config,euB2bVatRate:'invalid'}},
   {configuration:{...config,exportVatRate:'invalid'}},
   {configuration:{...config,defaultVatRate:'zw'}},
   {configuration:{...config,defaultVatRate:'0 WDT'}},
  ]) {
   const purchase={...sellfPurchase,...input};delete purchase.configuration;
   const configuration=input.configuration || config,sd={},refs={},logs=[];
   let name='Extract Purchase Data',json={body:{data:{customer:{email:purchase.email},
    product:{name:purchase.productName},order:{amount:12300,currency:'pln',sessionId:purchase.sessionId},
    invoice:{needsInvoice:purchase.needsInvoice,nip:purchase.invoiceNip,country:purchase.invoiceCountry}}}};
   for(let step=0;step<10;step++) {
    const node=d.nodes.find(n=>n.name===name);assert.ok(node,name);let port=0;
    const evaluate=s=>s.startsWith('={{')?new Function('$json',`return (${s.slice(3,-2).trim()})`)(json):s;
    if(node.type.endsWith('.code')) json=await run(d,name,json,{configuration,sd,refs,logs});
    else if(node.type.endsWith('.if') || node.type.endsWith('.switch')) {
     const conditions=node.parameters.conditions?.conditions || node.parameters.rules.values[0].conditions.conditions;
     port=conditions.every(c=>c.operator.operation==='true'?evaluate(c.leftValue)===true:evaluate(c.leftValue)!==c.rightValue)?0:1;
    } else if(node.type.endsWith('.respondToWebhook')) {
     const response=JSON.parse(evaluate(node.parameters.responseBody));
     assert.equal(response.skipped,true,JSON.stringify(input));
     assert.match(response.reason,/^pominięto: .+/);assert.equal(response.invoice,undefined);
     assert.deepEqual(sd,{});assert.deepEqual(logs,[]);break;
    } else assert.fail(`Unsafe purchase reached ${name}: ${JSON.stringify(input)}`);
    assert.notEqual(name,'Reserve Invoice','invalid purchase cannot reserve');
    refs[name]=json;name=d.connections[name]?.main[port]?.[0]?.node;
    assert.ok(step<9,'skip graph terminates');
   }
  }
 });
 test(`${f}: configured rates preserve net amounts and gateway rate types`,async()=>{
  for(const rate of ['np II','np I','0 WDT','0 EX',23,8,5,0,'zw']) {
   const configuration={...config,euB2bVatRate:rate,exportVatRate:'np I',exemptionBasis:'Art. 43'};
   for(const netAmount of [null,0,90.12]) {
    const result=await run(d,'Build KSeF Payload',{...sellfPurchase,invoiceCountry:'DE',invoiceNip:'DE123456789',netAmount},{configuration});
    assert.equal(result.invoice.items[0].vatRate,rate);
    const percentage=typeof rate==='number'?rate:0;
    assert.equal(result.invoice.items[0].unitPrice,Math.round((netAmount ?? 123/(1+percentage/100))*100)/100);
    if(rate==='zw') assert.equal(result.invoice.exemptionBasis,'Art. 43');
   }
  }
  const result=await run(d,'Build KSeF Payload',{...sellfPurchase,invoiceCountry:'US',invoiceNip:'12-3456789'},
   {configuration:{...config,exportVatRate:8}});
  assert.equal(result.invoice.items[0].vatRate,8);assert.equal(result.invoice.items[0].unitPrice,113.89);
 });
 test(`${f}: configuration and XSD EU country dictionary`,async()=>{
  const assignments=d.nodes.find(n=>n.name==='Configuration (EDIT ME)').parameters.assignments.assignments;
  assert.equal(assignments.find(a=>a.name==='euB2bVatRate')?.value,'np II');
  assert.equal(assignments.find(a=>a.name==='exportVatRate')?.value,'np I');
  const xsd=readFileSync(new URL('../../../src/KSeFGateway.Api.Tests/Schemas/schemat_FA(3)_v1-0E.xsd',import.meta.url),'utf8');
  const type=xsd.match(/<xsd:simpleType name="TKodyKrajowUE">([\s\S]*?)<\/xsd:simpleType>/)[1];
  const countries=[...type.matchAll(/<xsd:enumeration value="([A-Z]{2})"/g)].map(m=>m[1]);
  assert.ok(countries.includes('EL') && countries.includes('XI'));
  for(const country of countries.filter(c=>c!=='PL')) {
   const result=await run(d,'Build KSeF Payload',{...sellfPurchase,invoiceCountry:country==='EL'?'GR':country,invoiceNip:country+'123456789'});
   assert.equal(result.invoice.buyer.euVatNumber,country+'123456789');
   assert.equal(result.invoice.items[0].vatRate,'np II');
  }
 });
}
