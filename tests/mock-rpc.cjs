const readline=require('node:readline'); let initialized=false,held;
const reply=(id,result)=>process.stdout.write(JSON.stringify({id,result})+'\n');
readline.createInterface({input:process.stdin}).on('line',line=>{
  const message=JSON.parse(line);
  if(message.method==='initialize')return reply(message.id,{});
  if(message.method==='initialized'){initialized=true;return;}
  if(!initialized)throw new Error('Missing initialized notification');
  if(message.method==='test/a'){held=message.id;return;}
  if(message.method==='test/b'){reply(message.id,{name:'b'});reply(held,{name:'a'});return;}
  if(message.method==='test/error')return process.stdout.write(JSON.stringify({id:message.id,error:{code:-1}})+'\n');
  if(message.method==='test/exit')process.exit(0);
});
