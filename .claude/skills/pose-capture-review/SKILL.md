---
name: pose-capture-review
description: Review in-game character pose and animation captures for Tiền Tuyến (hero, enemies, menu/title poses, weapon and knife holds) before accepting a peer's handback or showing the user. Use this whenever a handback, screenshot sheet or capture folder shows a character pose, a weapon/knife grip, a retargeted clip, a stance or a "ours vs reference" comparison — even if the request only says "check the shots", "xem ảnh", "accept #N" or "does it look like the reference".
---

# Pose capture review

The user is a sharp visual reviewer and this is graded coursework where beauty comes first. When the user
finds a pose problem in a capture the lead already accepted, the review has failed. Captures usually get a
glance at the part the brief was about, while the defect sits somewhere else: the other limb, the posture
as a whole, or the gap between what the peer says and what the image shows. This skill makes the review
cover the whole figure, every time, before the verdict.

## 1. Look at the real pixels

- Open every image at its original resolution. A sheet that displays smaller than it is hides hands,
  wrists, fingers and clipping, so crop each tile (PIL) and open the crops.
- Open every tile of every image in the handback, not a sample.
- If you need a view that does not exist (a close-up of a hand, a side view, the ground), ask for it
  instead of inferring from the full shot.

## 2. Read the peer's claims as things to verify

List each concrete claim in the handback ("matches the reference", "palm flat on the waist", "no
clipping", "2 mm gap"). Each claim is either confirmed by a pixel you can point to, or it is not. A
measured number from a probe does not overrule what the image shows; if they disagree, say so.

## 3. Walk the whole figure, in every view

When there is a reference (a source-rig render, a stand-in skeleton, a photo the user sent, an earlier
accepted capture), compare ours and the reference side by side at the same angles. Go through every row
below for every view, whether or not the brief mentions it:

| Part | Look for |
|---|---|
| Overall posture | the silhouette and attitude it reads as, and whether that fits the user's intent |
| Spine, chest, head | lean, hunch, tilt, where the head looks |
| Shoulders | raised, collapsed, rolled forward |
| Left arm and right arm | elbow angle, distance from the torso, symmetry |
| Left wrist and right wrist | bend and roll compared with the reference |
| Left hand and right hand | finger curl and spread, palm direction, contact with what it should touch |
| Held object | contact with the hand, direction, clipping into body or limbs |
| Pelvis and legs | stance width, knee bend, weight |
| Feet | contact with ground or prop: floating, sinking, sliding |
| Props and scene | anything placed, missing, black or out of place |

Treat both sides of the body as separate rows. The limb the brief did not name is where defects survive.
If a limb differs from the reference by the same amount at every frame, report it as a likely constant
offset (avatar or bone mapping), without claiming it as the cause.

## 4. Judge against what the user wants

Matching the reference is necessary but not sufficient. Read the user's latest words about this shot and
the project memories, and ask whether the pose as a whole reads the way the user asked for. If it matches
the source and still looks wrong for the user, say so plainly.

## 5. Decide, and show the check

Before writing the verdict, write the table from step 3 in your reply to the peer (one short line per row:
fine / problem and where). This is what makes the review real rather than a glance.

- Accept only when every row is fine at full resolution, or the remaining issue is one the user already
  chose to keep.
- Otherwise send rework naming the part, the view, the file and what it should look like. Give the
  outcome, not the fix.
- When the user must choose between variants, send all of them, say which you recommend and what is still
  wrong with the others.
- Note scene oddities separately; don't block a pose on them and don't claim a cause you haven't shown.

## 6. Report to the user

In Vietnamese, short. Send the key images (full shot first, then the close crop). Say what was checked,
what is still imperfect, and the one question or next step for the user.
