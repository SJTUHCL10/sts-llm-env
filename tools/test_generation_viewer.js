// Run with: node tools/test_generation_viewer.js [card-text-parity-fixtures.json]
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class Element {
  constructor(tag) { this.tag = tag; this.children = []; this.textContent = ''; this.value = ''; this.handlers = {}; this.open = false; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  addEventListener(event, handler) { this.handlers[event] = handler; }
  setAttribute() {}
  text() { return this.textContent + this.children.map(child => child.text()).join(''); }
}
const page = fs.readFileSync(path.join(__dirname, 'generation_viewer.html'), 'utf8');
const source = page.match(/<script>([\s\S]*?)<\/script>/)[1];
const elements = new Map();
const context = vm.createContext({
  document: {createElement: tag => new Element(tag), getElementById: id => {
    if (!elements.has(id)) elements.set(id,new Element('div'));
    return elements.get(id);
  }},
  setInterval() {}, setTimeout() {},
  fetch: async () => ({ok:true,json:async () => ({directory:'fixture',sessions:[],errors:[]})})
});
vm.runInContext(source.replace(/    refresh\(\);\s*$/, ''),context);
const run = code => vm.runInContext(code,context);
const walk = node => [node,...node.children.flatMap(walk)];
assert.equal(run(`effectText({kind:'damage',target:'enemy',amount:10,on_fatal:[{kind:'gain_max_hp',amount:3},{kind:'gain_gold',amount:20}]})`),
  '造成10点伤害。斩杀：增加3点最大生命。获得20枚金币。');
assert.equal(run(`effectText({kind:'damage',target:'enemy',amount:1,on_fatal:[{kind:'damage',target:'random_enemy',amount:2,on_fatal:[{kind:'draw',amount:1}]}]})`),
  '造成1点伤害。斩杀：对随机敌人造成2点伤害。斩杀：抽1张牌。');
assert.equal(run(`effectText({kind:'lose_max_hp',amount:2})`),'减少2点最大生命。');
assert.equal(run(`effectText({kind:'lose_gold',amount:5})`),'失去5枚金币。');

const block = run(`disclosure('JSON',json({cards:[{forms:[{effects:[{kind:'draw',amount:2}]}]}]}))`);
assert.equal(block.open,false);
assert.equal(walk(block).filter(node => node.className === 'json-node').length,0);
block.open = true; block.handlers.toggle();
const branches = walk(block).filter(node => node.className === 'json-node');
assert.ok(branches.length >= 6);
assert.ok(branches.every(node => node.open));
branches.at(-1).open = false; branches.at(-1).handlers.toggle();
assert.equal(branches.at(-1).open,false);
assert.ok(block.text().includes('"amount": 2'));

const raw = '  {"cards": [\n  <script>model text</script>]}  ';
context.raw = raw;
const output = run(`disclosure('模型原始输出 / content',raw)`);
assert.equal(walk(output).find(node => node.tag === 'pre').textContent,raw);
const validOutput = run(`disclosure('content','  {"cards":[]}  ')`);
assert.equal(walk(validOutput).filter(node => node.tag === 'pre').length,0);
validOutput.open = true; validOutput.handlers.toggle();
assert.ok(validOutput.text().includes('"cards": []'));

const card = {name:'fixture',type:'attack',rarity:'common',forms:[
  {cost:{energy:1},effects:[{kind:'damage',target:'enemy',amount:7}]},
  {cost:{energy:1},effects:[{kind:'damage',target:'enemy',amount:10}]}
]};
context.card = card;
const historical = run('renderCard(card)');
assert.ok(historical.text().includes('造成7点伤害。'));
assert.ok(historical.text().includes('造成10点伤害。'));
const snapshot = run(`renderCard(card,['共享基础文本。','共享升级文本。'])`);
assert.ok(snapshot.text().includes('共享基础文本。'));
assert.ok(snapshot.text().includes('共享升级文本。'));
assert.ok(!snapshot.text().includes('造成7点伤害。'));
assert.ok(run(`formText({effects:[],rules:[{trigger:{event:'turn_start'},lifetime:'turn',turns:2,effects:[{kind:'draw',amount:1}]}]},'skill')`).includes('接下来的2个回合'));
for (const [cost,stat] of [[{energy_x:true},'paid_energy'],[{stars_x:true},'paid_stars']]) {
  context.xForm = {cost,effects:[
    {kind:'draw',amount:{stat}},
    {kind:'damage',target:'enemy',amount:5,repeat:{add:[{stat},1]}},
    {kind:'create_card',card:{id:'shiv'},count:{stat},to:'hand'}
  ]};
  assert.equal(run(`formText(xForm,'skill')`),'抽X张牌。\n重复以下效果(X + 1)次：造成5点伤害。\n将X张小刀添加到你的手牌。');
}
assert.equal(run(`ruleText({trigger:{event:'card_generated'},effects:[{kind:'block',amount:2}]},'power')`),'每当你生成一张牌，获得2点格挡。');
assert.equal(run(`numberText({sub:[{stat:'orb_capacity'},{stat:'orb_count'}]})`),'(你的充能球栏位数 - 你的充能球数)');
assert.equal(run(`numberText({add:[{stat:'orb_capacity'},{mul:[{stat:'orb_count'},-1]}]})`),'(你的充能球栏位数 - 你的充能球数)');
assert.equal(run(`effectText({kind:'orb_slots',amount:-1})`),'失去1个充能球栏位。');
assert.equal(run(`effectText({kind:'orb_slots',amount:2})`),'获得2个充能球栏位。');
context.sameCondition = {op:'ge',left:{stat:'orb_count'},right:{stat:'orb_capacity'}};
assert.equal(run(`formText({effects:[{kind:'gain_energy',amount:2,condition:sameCondition},{kind:'draw',amount:2,condition:sameCondition}]},'skill')`),'若你的充能球数 ≥ 你的充能球栏位数，获得2点能量，抽2张牌。');
assert.equal(run(`formText({effects:[{kind:'gain_energy',amount:2,condition:{op:'ge',left:{stat:'energy'},right:2}},{kind:'draw',amount:2,condition:{op:'ge',left:{stat:'energy'},right:2}}]},'skill').split('若').length`),3);

elements.get('status').value = 'all';
run(`current = {summary:{file:'fixture.jsonl'},requests:[{revision:1,status:'failed',response:{content:raw},result:{reason:'completion_token_limit'}}],events:[]}; renderDetail();`);
assert.ok(elements.get('detail').text().includes(raw));
run(`current.requests[0].response.content = '  {"cards":[]}  '; renderDetail();`);
const contentBlock = elements.get('detail').children.find(node => node.tag === 'details' && node.text().includes('模型原始输出 / content'));
assert.ok(walk(contentBlock).some(node => node.className === 'json-tree'));
run(`current.requests[0].response = {}; renderDetail();`);
assert.ok(elements.get('detail').text().includes('旧版日志未保存'));

if (process.argv[2]) {
  const fixtures = JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
  for (const fixture of fixtures) {
    context.fixture = fixture;
    fixture.card.forms.forEach((form,index) => {
      assert.equal(run(`formText(fixture.card.forms[${index}],fixture.card.type)`),fixture.texts[index] || '无效果。',fixture.card.name+' form '+index);
    });
  }
  console.log(`PASS historical text matches Core/CardText for ${fixtures.length} definitions`);
}
console.log('PASS nested JSON expansion, manual collapse, raw content, snapshots and historical rendering');

async function testRunFilters() {
  const session = (file,run_key,timestamp_utc,ready=1,failed=0) => ({
    file,key:file,run_key,timestamp_utc,floor:7,cards:[file],models:[],reasons:[],
    requests:ready+failed,ready,failed,pending:0,discarded:0,warning_count:0
  });
  const newest = session('new-2.jsonl','new','2026-10-13T02:00:00Z');
  const oldest = session('old.jsonl','old','2026-10-12T01:00:00Z',0,1);
  context.sessions = [newest,session('new-1.jsonl','new','2026-10-13T01:00:00Z'),oldest,
    session('legacy.jsonl',null,null),session('legacy-empty.jsonl','',null)];
  let detailsRead = [];
  context.fetch = async url => ({ok:true,json:async () => {
    if (url === '/api/sessions') return {directory:'fixture',sessions:context.sessions,errors:[]};
    const file = decodeURIComponent(url.split('file=')[1]);
    detailsRead.push(file);
    return {summary:context.sessions.find(s => s.file === file),requests:[],events:[]};
  }});
  run(`index = []; activeRun = null; activeFile = null; current = null; updateRuns();`);
  assert.equal(run('activeRun'),null,'empty logs must not prevent choosing the latest run later');
  await run('refresh()');
  assert.equal(run('activeRun'),'run:new');
  assert.equal(run('activeFile'),'new-2.jsonl');
  assert.equal(elements.get('sessions').children.length,2);
  assert.ok(elements.get('stats').text().includes('2 场战斗 · 2 次请求 · 成功 2 · 失败 0'));
  assert.equal(elements.get('run').children.length,4,'one option per run plus all and unknown');
  assert.ok(elements.get('run').children[1].text().includes(run(`time('2026-10-13T01:00:00Z')`)));
  assert.ok(elements.get('run').children[3].text().includes('未记录局号（2 场战斗）'));

  elements.get('run').value = 'run:old';
  await elements.get('run').onchange();
  assert.equal(run('activeFile'),'old.jsonl');
  assert.equal(run('current.summary.run_key'),'old');
  assert.equal(elements.get('sessions').children.length,1);
  assert.ok(elements.get('stats').text().includes('1 场战斗 · 1 次请求 · 成功 0 · 失败 1'));
  elements.get('status').value = 'ready';
  await elements.get('status').onchange();
  assert.equal(run('activeFile'),null);
  assert.ok(elements.get('detail').text().includes('没有匹配的记录'));
  await run('refresh()');
  assert.equal(run('activeFile'),null,'refresh must not open a record outside the filters');
  assert.equal(elements.get('run').children.length,4,'status filters must not remove run options');
  elements.get('status').value = 'failed';
  await elements.get('status').onchange();
  assert.equal(run('activeFile'),'old.jsonl');

  context.sessions.unshift(session('latest.jsonl','latest','2026-10-13T03:00:00Z'));
  await run('refresh()');
  assert.equal(run('activeRun'),'run:old','auto refresh must preserve the chosen run');
  assert.equal(elements.get('run').value,'run:old');
  elements.get('search').value = 'new-1';
  elements.get('status').value = 'all';
  elements.get('run').value = 'run:new';
  await elements.get('run').onchange();
  assert.equal(run('activeFile'),'new-1.jsonl','search and run filters must combine');
  elements.get('search').value = '';
  elements.get('run').value = 'unknown';
  await elements.get('run').onchange();
  assert.equal(elements.get('sessions').children.length,2);
  assert.equal(run('activeFile'),'legacy.jsonl');
  elements.get('run').value = '';
  await elements.get('run').onchange();
  assert.equal(elements.get('sessions').children.length,6);
  await run('refresh()');
  assert.equal(run('activeRun'),'','all runs selection must survive refresh');

  elements.get('run').value = 'run:old';
  await elements.get('run').onchange();
  context.sessions = context.sessions.filter(s => s.run_key !== 'old');
  await run('refresh()');
  assert.equal(run('activeRun'),'run:latest','deleted run must fall back to the most recent run');
  assert.equal(run('activeFile'),'latest.jsonl');
  assert.ok(detailsRead.includes('old.jsonl'));
  console.log('PASS run grouping, defaults, combined filters, unknown runs, detail selection and refresh');
}
testRunFilters().catch(error => { console.error(error); process.exitCode = 1; });
