// Static marginal cost of opening one sample, from the chunk DAG the SCC split produced.
const fs=require('fs'), path=require('path');
const site='/home/user/tesserae/Tesserae.Tests/bin/Debug/netstandard2.0/tps';
const out='site-scc';
const boot=fs.readFileSync(out+'/boot.mjs','utf8');
const MAN=JSON.parse(boot.match(/const MAN = (\{.*?\});\n/s)[1]);
// chunk -> deps, read from the emitted module headers
const chunks={};
for(const f of fs.readdirSync(out+'/c')){
  const src=fs.readFileSync(out+'/c/'+f,'utf8');
  const deps=[...src.matchAll(/^import '\.\/(c\d+\.mjs)';$/gm)].map(m=>m[1]);
  chunks[f]={deps,size:fs.statSync(out+'/c/'+f).size};
}
const eager=new Set([...boot.matchAll(/^import '\.\/c\/(c\d+\.mjs)';$/gm)].map(m=>m[1]));
// close the eager set
{const st=[...eager]; while(st.length){const c=st.pop(); for(const d of chunks[c].deps) if(!eager.has(d)){eager.add(d);st.push(d);} }}
const eagerBytes=[...eager].reduce((a,c)=>a+chunks[c].size,0);

const samples=Object.keys(MAN).filter(n=>/Samples\.[A-Za-z]+Sample$/.test(n));
const costs=samples.map(n=>{
  const start=MAN[n].c.replace('./c/','');
  const seen=new Set([start]), st=[start];
  while(st.length){const c=st.pop(); for(const d of chunks[c].deps) if(!seen.has(d)&&!eager.has(d)){seen.add(d);st.push(d);} }
  const bytes=[...seen].filter(c=>!eager.has(c)).reduce((a,c)=>a+chunks[c].size,0);
  return {n:n.split('.').pop(),kb:bytes/1024,chunks:[...seen].filter(c=>!eager.has(c)).length};
}).sort((a,b)=>a.kb-b.kb);
const kb=x=>x.toFixed(0)+' KB';
console.log('eager (initial) payload:', kb(eagerBytes/1024), 'over', eager.size, 'chunks');
console.log('samples measured:', costs.length);
console.log('  cheapest :', costs[0].n, kb(costs[0].kb));
console.log('  median   :', kb(costs[Math.floor(costs.length/2)].kb));
console.log('  p90      :', kb(costs[Math.floor(costs.length*0.9)].kb));
console.log('  worst    :', costs[costs.length-1].n, kb(costs[costs.length-1].kb));
console.log('  total if all opened:', kb(costs.reduce((a,c)=>a+c.kb,0)/1)); // overlapping, so > lazy total
