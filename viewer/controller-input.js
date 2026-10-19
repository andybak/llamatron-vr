// xr-standard reserves axes 2/3 for the primary thumbstick.
export function controllerSources(browser,native,ageMs) {
  const fresh=ageMs<250?native:[];
  return [...fresh,...browser.filter(source=>!fresh.some(candidate=>candidate.handedness===source.handedness))];
}

export function thumbstick(gamepad) {
  const axes=gamepad.axes||[];
  if(gamepad.mapping==='xr-standard'&&axes.length>=4)return [axes[2]||0,axes[3]||0];
  let selected=[0,0];
  for(let i=0;i+1<axes.length;i+=2){
    const pair=[axes[i]||0,axes[i+1]||0];
    if(Math.hypot(...pair)>Math.hypot(...selected))selected=pair;
  }
  return selected;
}

export function controllerBits(sources, deadzone=.25) {
  let movement=[0,0],leftMovement=null,fire=false;
  for(const source of sources){
    const pad=source.gamepad;if(!pad)continue;
    const pair=thumbstick(pad);
    if(source.handedness==='left'&&pair.some(axis=>Math.abs(axis)>deadzone))leftMovement=pair;
    if(Math.hypot(...pair)>Math.hypot(...movement))movement=pair;
    fire ||= !!pad.buttons?.[0]?.pressed;
  }
  // An active left stick wins over right-stick drift or a stronger deflection.
  // Re-evaluate the current sources each time so a returning left hand wins immediately.
  const [x,y]=leftMovement||movement;
  return (x < -deadzone?4:0)|(x > deadzone?8:0)|(y < -deadzone?1:0)|(y > deadzone?2:0)|(fire?128:0);
}
