// Run with: node tools/test_generation_viewer.js [card-text-parity-fixtures.json]
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class Element {
  constructor(tag) { this.tag = tag; this.children = []; this.textContent = ''; this.handlers = {}; this.open = false; }
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
const output = run(`disclosure('模型原始输出 / content',raw,false,'',false)`);
assert.equal(walk(output).find(node => node.tag === 'pre').textContent,raw);
const validOutput = run(`disclosure('content','  {"cards":[]}  ',false,'',false)`);
assert.equal(walk(validOutput).find(node => node.tag === 'pre').textContent,'  {"cards":[]}  ');
assert.equal(walk(validOutput).filter(node => node.className === 'json-node').length,0);

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

elements.get('status').value = 'all';
run(`current = {summary:{file:'fixture.jsonl'},requests:[{revision:1,status:'failed',response:{content:raw},result:{reason:'completion_token_limit'}}],events:[]}; renderDetail();`);
assert.ok(elements.get('detail').text().includes(raw));
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
