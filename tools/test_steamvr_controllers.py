"""Check native role/axis conversion without taking over a live headset."""
from steamvr_controllers import SteamVRControllers

reader = SteamVRControllers.__new__(SteamVRControllers)
reader.role_index = lambda role: role
reader.connected = lambda index: True
reader.int_property = lambda index, prop, error: 2 if prop == 3002 else 3
def state(index, output, size):
    output._obj.axes[0].x = -.5 if index == 1 else .8
    output._obj.axes[0].y = .7
    output._obj.pressed = 1 << 33
    return True
reader.controller_state = state
sources = reader.snapshot()
assert [s['handedness'] for s in sources] == ['left', 'right']
assert abs(sources[0]['gamepad']['axes'][2] + .5) < .001
assert abs(sources[0]['gamepad']['axes'][3] + .7) < .001
assert sources[1]['gamepad']['buttons'][0]['pressed']
reader.connected = lambda index: index == 2
assert [s['handedness'] for s in reader.snapshot()] == ['right']
print('SteamVR conversion passed: roles, joystick axis type, Y direction, trigger and disconnect.')
