# BeatMap
## Introduction
This is a 4K (4-Keys) BeatMap for a rhythm game simulator for execrise and fun.
## How to use
Drag your beatmap to executable file or input following command:

`BeatMap "path\to\your\beatmap.bm"`

## BeatMap(.bm) formating description
A BeatMap file is a plain text file with the following structure:

```
Title;
Artist;
BPM;
Notes;
```

And in `Notes`, the format is as follows:

A note unit is represented as:
```
a num | a binary (start with 0b prefix) | a string (contain 'd', 'f', 'j', 'k' and 0 < length <= 4>) | (empty)
```

And all of `Notes` part is a sequence of note units separated by semicolons (`,`).

> [!IMPORTANT]
> Each note unit in game is separated by a fixed time interval, which is calculated as `60000 / BPM` milliseconds, just a quarter note duration.

---
Have fun!