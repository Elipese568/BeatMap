# BeatMap
## Introduction
This is a 4K (4-Keys) BeatMap for a rhythm game simulator for execrise and fun.
## How to use
Drag your beatmap to executable file or input following command:

`BeatMap "path\to\your\beatmap.bm"`

## BeatMap(.bm) formatting description
A BeatMap file is a plain text file with the following structure:

```
Title;
Artist;
BPM;
Notes;
```

### Notes formatting

A note unit is represented as:
```
a num | a binary (start with 0b prefix) | (empty)
```

And all of `Notes` part is a sequence of note units separated by semicolons (`,`).

### Attributes


---
Have fun!