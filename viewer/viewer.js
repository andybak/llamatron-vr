import {controllerBits,controllerSources} from './controller-input.js';
import {decodeEffectPlanes} from './native-effects.js';
const canvas = document.querySelector('#scene');
const status = document.querySelector('#status');
const vrButton = document.querySelector('#vr');
const gl = canvas.getContext('webgl', { antialias: true, alpha: false, xrCompatible: true });
if (!gl) throw new Error('WebGL is unavailable');
const vertex = `attribute vec3 position; attribute vec2 uv;
uniform mat4 projection, view, model; varying vec2 texcoord;
void main(){texcoord=uv;gl_Position=projection*view*model*vec4(position,1.0);}`;
const fragment = `precision mediump float; uniform sampler2D image; uniform vec4 tint;
varying vec2 texcoord; void main(){vec4 c=texture2D(image,texcoord)*tint;
if(c.a<0.5)discard;gl_FragColor=c;}`;
function shader(type, source) {
  const s = gl.createShader(type); gl.shaderSource(s, source); gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
  return s;
}
const program = gl.createProgram();
gl.attachShader(program, shader(gl.VERTEX_SHADER, vertex));
gl.attachShader(program, shader(gl.FRAGMENT_SHADER, fragment)); gl.linkProgram(program);
if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
gl.useProgram(program);
const uniforms = Object.fromEntries(['projection', 'view', 'model', 'tint'].map(k => [k, gl.getUniformLocation(program, k)]));
const buffer = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([
  -.5,0,0,0,0, .5,0,0,1,0, -.5,1,0,0,1,
  -.5,1,0,0,1, .5,0,0,1,0, .5,1,0,1,1,
]), gl.STATIC_DRAW);
const attributes = [['position',3,0],['uv',2,12]].map(([name,size,offset])=>[gl.getAttribLocation(program,name),size,offset]);
function bindGeometry(geometry) {
  gl.bindBuffer(gl.ARRAY_BUFFER,geometry);
  for(const [location,size,offset] of attributes){gl.enableVertexAttribArray(location);gl.vertexAttribPointer(location,size,gl.FLOAT,false,20,offset);}
}
bindGeometry(buffer);
gl.enable(gl.DEPTH_TEST); gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
function texture(source) {
  const t = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, t);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE); return t;
}
const floorCanvas = document.createElement('canvas'); floorCanvas.width = 640; floorCanvas.height = 480;
const paint = floorCanvas.getContext('2d'); paint.fillStyle = '#102126'; paint.fillRect(0,0,640,480);
paint.strokeStyle = '#1d373c'; paint.lineWidth = 1;
for (let x=0;x<=640;x+=32){paint.beginPath();paint.moveTo(x,0);paint.lineTo(x,480);paint.stroke();}
for (let y=0;y<=480;y+=32){paint.beginPath();paint.moveTo(0,y);paint.lineTo(640,y);paint.stroke();}
paint.strokeStyle = '#70b7a4'; paint.lineWidth = 6; paint.strokeRect(3,3,634,474);
const floorTexture = texture(floorCanvas);
function extrudeSprite(img) {
  const c=document.createElement('canvas');c.width=img.width;c.height=img.height;
  const context=c.getContext('2d');context.drawImage(img,0,0);
  const pixels=context.getImageData(0,0,c.width,c.height).data, vertices=[];
  const opaque=(x,y)=>x>=0&&y>=0&&x<c.width&&y<c.height&&pixels[(y*c.width+x)*4+3]>=128;
  const face=(points,u,v)=>{for(const i of [0,1,2,0,2,3])vertices.push(...points[i],u,v);};
  const depth=.006;
  for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++){
    if(!opaque(x,y))continue;
    const left=x/c.width-.5,right=(x+1)/c.width-.5,top=1-y/c.height,bottom=1-(y+1)/c.height;
    const u=(x+.5)/c.width,v=1-(y+.5)/c.height;
    if(!opaque(x-1,y))face([[left,bottom,-depth],[left,bottom,depth],[left,top,depth],[left,top,-depth]],u,v);
    if(!opaque(x+1,y))face([[right,bottom,depth],[right,bottom,-depth],[right,top,-depth],[right,top,depth]],u,v);
    if(!opaque(x,y-1))face([[left,top,depth],[right,top,depth],[right,top,-depth],[left,top,-depth]],u,v);
    if(!opaque(x,y+1))face([[left,bottom,-depth],[right,bottom,-depth],[right,bottom,depth],[left,bottom,depth]],u,v);
  }
  const geometry=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,geometry);
  gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(vertices),gl.STATIC_DRAW);
  bindGeometry(buffer);
  return {geometry,count:vertices.length/5};
}
let assets = new Map(), sprites = [], total = 0, feedError = null, paused = true;
const effectsCanvas=document.createElement('canvas');effectsCanvas.width=320;effectsCanvas.height=200;
const effectsContext=effectsCanvas.getContext('2d'),effectsImage=effectsContext.createImageData(320,200);
let effectsTexture=null,effectsPublication=null,nativeEffectsActive=false;
let nativeControllerSources=[],nativeControllerTime=-Infinity;
function updateEffects(frame){
  const effects=frame.native_effects;nativeEffectsActive=!!effects;
  if(!effects||effects.publication===effectsPublication)return;
  const encoded=atob(effects.planes),planes=Uint8Array.from(encoded,c=>c.charCodeAt(0));
  effectsImage.data.set(decodeEffectPlanes(planes,effects.palette));
  effectsContext.putImageData(effectsImage,0,0);
  if(!effectsTexture)effectsTexture=texture(effectsCanvas);
  else{
    gl.bindTexture(gl.TEXTURE_2D,effectsTexture);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,effectsCanvas);
  }
  effectsPublication=effects.publication;
}
const manifest = await fetch('/artifacts/sprites/sprites.json').then(r => r.json());
for (const item of manifest.sprites) assets.set(item.id, { ...item, texture: null, loading: false });
status.textContent='Loading sprite graphics…';
await Promise.all([...assets.values()].map(a=>new Promise(resolve=>{
  const img=new Image(); a.loading=true;
  img.onload=()=>{a.texture=texture(img);a.sides=extrudeSprite(img);resolve();};
  img.onerror=()=>{a.error=true;resolve();};
  img.src=`/artifacts/sprites/${a.bank}/${a.id}.png`;
})));
function asset(id) {
  const a = assets.get(id);
  if (a && !a.texture && !a.loading) {
    a.loading = true; const img = new Image();
    img.onload = () => { a.texture = texture(img); a.sides=extrudeSprite(img); };
    img.onerror = () => { a.error = true; };
    img.src = `/artifacts/sprites/${a.bank}/${a.id}.png`;
  }
  return a;
}
async function poll() {
  try {
    const response = await fetch('/frame', { cache: 'no-store' });
    if (!response.ok) throw new Error('Arena connection lost');
    const frame = await response.json(); total = frame.total_events; feedError = null; paused=!!frame.paused;
    const missing=[...new Set(frame.sprites.filter(s=>!(frame.native_effects&&s.native_effect)&&!assets.get(s.sprite_id)?.texture).map(s=>s.sprite_id))];
    if(missing.length)throw new Error(`Missing sprite graphics: ${missing.slice(0,3).join(', ')}${missing.length>3?'…':''}`);
    const unique = new Map();
    for (const s of frame.sprites) unique.set(`${s.sprite_id}:${s.x}:${s.y}`, s);
    updateEffects(frame);
    nativeControllerSources=frame.native_controllers?.sources||[];
    nativeControllerTime=performance.now()-(frame.native_controllers?.age_ms??Infinity);
    sprites = [...unique.values()]; sprites.forEach(s => asset(s.sprite_id));
    status.textContent = sprites.length ? `${sprites.length} sprites in the arena · ${paused?'paused':'playing'}` : 'Waiting for the title screen…';
    document.querySelector('#pause').textContent=paused?'Resume game':'Pause game';
    document.querySelector('#audio-status').textContent=frame.audio_enabled?'Sound Blaster audio enabled':'Sound disabled in test copy';
  } catch (error) { feedError = error.message; status.textContent = error.message; }
  setTimeout(poll, 25);
}
poll();
document.querySelector('#pause').addEventListener('click',()=>fetch('/pause',{
  method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({paused:!paused})
}).catch(()=>{}));
function perspective(aspect) {
  const f = 1/Math.tan(Math.PI/6), near=.02, far=50;
  return new Float32Array([f/aspect,0,0,0, 0,f,0,0, 0,0,(far+near)/(near-far),-1, 0,0,2*far*near/(near-far),0]);
}
function normalize(a) { const n=Math.hypot(...a); return a.map(v=>v/n); }
function cross(a,b) { return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]; }
function lookAt(eye, target) {
  const z=normalize(eye.map((v,i)=>v-target[i])), x=normalize(cross([0,1,0],z)), y=cross(z,x);
  const dot=a=>-a.reduce((sum,v,i)=>sum+v*eye[i],0);
  return new Float32Array([x[0],y[0],z[0],0,x[1],y[1],z[1],0,x[2],y[2],z[2],0,dot(x),dot(y),dot(z),1]);
}
function model(x,y,z,width,height,flat=false) {
  return new Float32Array([width,0,0,0, 0,flat?0:height,flat?-height:0,0, 0,flat?1:0,flat?0:1,0, x,y,z,1]);
}
function quad(tex, transform) {
  bindGeometry(buffer);
  gl.bindTexture(gl.TEXTURE_2D,tex); gl.uniformMatrix4fv(uniforms.model,false,transform); gl.drawArrays(gl.TRIANGLES,0,6);
}
// Original directional shot artwork: four orientations, normal and hot shots.
// Its image Y axis describes arena depth, unlike an upright character sprite.
const arenaShots=new Set(['main-1a12','main-1a3c','main-1ac0','main-1b44',
  'main-1bc8','main-1bf2','main-1c76','main-1cfa']);
function draw(projection, view) {
  gl.uniformMatrix4fv(uniforms.projection,false,projection); gl.uniformMatrix4fv(uniforms.view,false,view);
  gl.uniform4f(uniforms.tint,1,1,1,1); quad(floorTexture,model(0,.65,-1,2,1.5,true));
  const raised = document.querySelector('#raised').checked;
  if(nativeEffectsActive&&effectsTexture)quad(effectsTexture,model(0,raised ? .72 : .660,-1,2,1.5,true));
  for (const s of sprites) {
    if(nativeEffectsActive&&s.native_effect)continue;
    const a=asset(s.sprite_id); if (!a?.texture) continue;
    const width=a.width*.00625, height=a.height*.0075;
    const x=(s.x-160)*.00625, z=-2.5+s.y*.0075;
    if (s.bank==='g3') quad(a.texture,model(x,1.38-s.y*.003,-2.53,width,height));
    else if (raised) {
      const flat=arenaShots.has(s.sprite_id), y=flat ? .72 : .658, originZ=flat?z+height/2:z;
      quad(a.texture,model(x,y+(flat ? .006 : 0),originZ+(flat?0:.006),width,height,flat));
      quad(a.texture,model(x,y-(flat ? .006 : 0),originZ-(flat?0:.006),width,height,flat));
      if(a.sides){
        gl.uniform4f(uniforms.tint,.62,.62,.62,1);
        gl.uniformMatrix4fv(uniforms.model,false,model(x,y,originZ,width,height,flat));
        bindGeometry(a.sides.geometry);gl.drawArrays(gl.TRIANGLES,0,a.sides.count);
        gl.uniform4f(uniforms.tint,1,1,1,1);
      }
    }
    else quad(a.texture,model(x,.658,z+height/2,width,height,true));
  }
}
let yaw=.15, pitch=.85, distance=2.5, drag=null, session=null, reference=null;
canvas.addEventListener('pointerdown',e=>{drag=[e.clientX,e.clientY];canvas.setPointerCapture(e.pointerId);});
canvas.addEventListener('pointermove',e=>{if(!drag)return;yaw-=(e.clientX-drag[0])*.006;pitch=Math.max(.15,Math.min(1.5,pitch+(e.clientY-drag[1])*.006));drag=[e.clientX,e.clientY];});
canvas.addEventListener('pointerup',()=>drag=null);
canvas.addEventListener('wheel',e=>{e.preventDefault();distance=Math.max(1.2,Math.min(5,distance+e.deltaY*.002));},{passive:false});
let keyboardBits=0, xrBits=0, lastSentBits=0, inputInFlight=false, inputPending=false;
let desiredInput={bits:0,scan:null}, inputError=null, lastInputAck=0;
async function pumpInput(){
  if(inputInFlight||!inputPending)return;
  inputInFlight=true;inputPending=false;
  const packet={...desiredInput},abort=new AbortController(),timer=setTimeout(()=>abort.abort(),2000);
  let succeeded=false;
  try{
    const response=await fetch('/input',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(packet),signal:abort.signal});
    if(!response.ok)throw new Error(`Input connection failed (${response.status})`);
    const result=await response.json();if(!result.ok)throw new Error(result.error||'Input rejected');
    lastSentBits=packet.bits;lastInputAck=performance.now();inputError=null;succeeded=true;
  }catch(error){inputError=error.message;inputPending=true;}
  finally{
    clearTimeout(timer);inputInFlight=false;
    document.querySelector('#input-status').textContent=inputError||'';
    if(inputPending&&succeeded)pumpInput();
  }
}
function sendInput(scan=null,force=false) {
  const bits=keyboardBits|xrBits;
  if(bits===desiredInput.bits&&scan===null&&!force)return;
  desiredInput={bits,scan};inputPending=true;pumpInput();
}
setInterval(()=>{
  // Also rescan if the XR animation callback briefly stalls.
  if(session)controllerInput(performance.now());
},500);
setInterval(()=>{
  if(inputPending)pumpInput();else if(session||keyboardBits||inputError)sendInput(null,true);
},100);
const keys={ArrowLeft:[4,0x4b],ArrowRight:[8,0x4d],ArrowUp:[1,0x48],ArrowDown:[2,0x50],Space:[128,0x39],KeyP:[0,0x19],KeyO:[0,0x18]};
for(const type of ['keydown','keyup']) addEventListener(type,e=>{
  const mapping=keys[e.code];if(!mapping)return;e.preventDefault();if(e.repeat)return;
  const down=type==='keydown';keyboardBits=down?keyboardBits|mapping[0]:keyboardBits&~mapping[0];
  sendInput(mapping[1]|(down?0:128));
});
addEventListener('blur',()=>{keyboardBits=0;sendInput();});
const start=document.querySelector('#start');
start.addEventListener('pointerdown',e=>{e.preventDefault();start.setPointerCapture(e.pointerId);keyboardBits|=128;sendInput(0x39);});
for(const type of ['pointerup','pointercancel'])start.addEventListener(type,()=>{keyboardBits&=~128;sendInput(0xb9);});
let lastControllerDiagnostic=0,controllerSourceChanges=0;
function controllerInput(time) {
  const browserSources=Array.from(session?.inputSources||[]);
  const nativeSources=performance.now()-nativeControllerTime<250?nativeControllerSources:[];
  const sources=controllerSources(browserSources,nativeSources,performance.now()-nativeControllerTime);
  const bits=session?.visibilityState==='hidden'?0:controllerBits(sources);
  if(bits!==xrBits){
    const before=xrBits;xrBits=bits;const spaceChanged=(before^bits)&128;
    const scan=spaceChanged?(bits&128?0x39:0xb9):(bits&4?0x4b:bits&8?0x4d:bits&1?0x48:bits&2?0x50:null);
    sendInput(scan);
  }
  if(time-lastControllerDiagnostic>250){
    canvas.dataset.controllerInput=JSON.stringify({bits,acknowledgedBits:lastSentBits,ackAgeMs:Math.round(performance.now()-lastInputAck),error:inputError,
      visibility:session?.visibilityState,sourceChanges:controllerSourceChanges,browserHands:browserSources.map(s=>s.handedness),nativeHands:nativeSources.map(s=>s.handedness),
      sources:sources.map(s=>({hand:s.handedness,profiles:s.profiles,axes:Array.from(s.gamepad?.axes||[]),mapping:s.gamepad?.mapping}))});
    lastControllerDiagnostic=time;
  }
}
function desktop() {
  if(session)return;
  const ratio=Math.min(devicePixelRatio,2), width=Math.round(innerWidth*ratio), height=Math.round(innerHeight*ratio);
  if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;}
  gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,canvas.width,canvas.height);
  gl.clearColor(.025,.035,.055,1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
  const target=[0,.65,-1.75];const eye=[Math.sin(yaw)*Math.cos(pitch)*distance,target[1]+Math.sin(pitch)*distance,target[2]+Math.cos(yaw)*Math.cos(pitch)*distance];
  draw(perspective(canvas.width/canvas.height),lookAt(eye,target));requestAnimationFrame(desktop);
}
async function xrFrame(time,frame) {
  if(!session)return;session.requestAnimationFrame(xrFrame);controllerInput(time);
  const pose=frame.getViewerPose(reference);if(!pose)return;
  const layer=session.renderState.baseLayer;gl.bindFramebuffer(gl.FRAMEBUFFER,layer.framebuffer);
  gl.clearColor(.025,.035,.055,1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
  for(const view of pose.views){const viewport=layer.getViewport(view);gl.viewport(viewport.x,viewport.y,viewport.width,viewport.height);draw(view.projectionMatrix,view.transform.inverse.matrix);}
}
vrButton.addEventListener('click',async()=>{
  try {
    const active=await navigator.xr.requestSession('immersive-vr',{optionalFeatures:['local-floor']});
    await gl.makeXRCompatible();session=active;
    controllerSourceChanges=0;
    session.addEventListener('inputsourceschange',()=>{
      controllerSourceChanges++;lastControllerDiagnostic=0;
      controllerInput(performance.now());sendInput(null,true);
    });
    session.addEventListener('visibilitychange',()=>{
      controllerInput(performance.now());sendInput(null,true);
    });
    session.updateRenderState({baseLayer:new XRWebGLLayer(session,gl)});
    try{reference=await session.requestReferenceSpace('local-floor');}
    catch{reference=(await session.requestReferenceSpace('local')).getOffsetReferenceSpace(new XRRigidTransform({y:-1.6}));}
    session.addEventListener('end',()=>{session=null;xrBits=0;sendInput();requestAnimationFrame(desktop);});
    controllerInput(performance.now());sendInput(null,true);
    session.requestAnimationFrame(xrFrame);
  }catch(error){status.textContent=`VR could not start: ${error.message}`;session=null;requestAnimationFrame(desktop);}
});
try {const supported=await navigator.xr?.isSessionSupported('immersive-vr');vrButton.disabled=!supported;vrButton.textContent=supported?'Enter VR':'Connect a PC VR headset to enter VR';}
catch{vrButton.textContent='VR unavailable in this browser';}
window.llamatronDiagnostics=()=>({spriteCount:sprites.length,totalEvents:total,loadedTextures:[...assets.values()].filter(a=>a.texture).length,glError:gl.getError(),feedError,xrActive:!!session});
requestAnimationFrame(desktop);
