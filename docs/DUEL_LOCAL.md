# Playing the 1v1 duel on one machine

Double-click `tools/duel/play-duel-local.cmd`. Two game windows open side by side. In each, skip the intro and
choose ĐỐI KHÁNG 1V1. In the left window press 1 to host. In the right window press 2, type 127.0.0.1 and press
Enter. The match starts once both arenas are built. If Windows Firewall asks the first time, either answer works
on one machine.

To rebuild the player first, close the Unity Editor and run `play-duel-local.cmd -Build` (10-20 minutes).
Without `-Build` it uses the last build in `Builds/Windows`.

Both windows use the Misty lighting: a low warm sun and mist over the jungle. Across the whole field the opponent
looks soft, and someone crouched in a parapet's shade is hard to pick out; at the usual 10-25 m between the works
it makes little difference. If spotting feels too hard, run `play-duel-local.cmd -Lighting Overcast`, which is
brighter and flatter; nothing else changes.

Only the window you clicked last takes the keyboard and mouse; the other keeps running. Click a window to play
that side; press Esc to free the mouse, then click the other window. Esc does not leave the match, and neither
window pauses: the side you are not playing stands still and can be shot.

## Controls

| Action | Keys |
|---|---|
| Move | WASD or arrows, left stick |
| Look | mouse, right stick |
| Fire | left mouse button, right trigger |
| Aim down sights | right mouse button, left trigger |
| Jump | Space, A |
| Crouch | Left Ctrl or C, R3 |
| Walk | Shift |
| Reload | R, X |
| Guns 1-3 / knife | 1, 2, 3 / 5 or F |
| Last weapon / next | Q / mouse wheel, Y |
| Free the mouse | Esc (click to take it back) |

First to win two rounds takes the match. A round lasts 90 seconds after a 3 second countdown; when time runs out
the soldier with more health wins it, and a drawn round counts for nobody. To leave a match, finish it or close
the window. Closing a window during a match forfeits it; when the right window closes, the left one just shows
a normal victory screen, without saying the other side left.

## Two machines

Start the game normally on both, choose ĐỐI KHÁNG 1V1, press 1 on one to host and give the other the address
it shows; on the other press 2, type the address, Enter. Windows Firewall asks the host once; allow it, or the other machine cannot reach it.

## Known unfinished

- The opponent's muzzle flash is guessed from their fire button, not sent shot by shot.
- Two separate machines over a real LAN have not been tried yet.
- Seen from the other window, the soldier's cheek does not rest on the rifle stock: there is a gap of about 4-6 cm
  when they aim level, and the cap brim touches the stock when they aim down.
