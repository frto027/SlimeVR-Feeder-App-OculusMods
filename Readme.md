# Describe

When I play games without steam vr, which means I run games with Oculus native API, I can get better performance, but SlimeVR won't work.

This is a SlimeVR Feeder App used for some oculus (link) native games, which is written in C#. By using the plugin, SlimeVR will work without SteamVR.

But Oculus doesn't support trackers, it's the next problem. With this plugin, slimeVR knows your Headset or Controller position, but the game won't knows the slimeVR tracker position. Future work should be done.

Current work on BeatSaber.

You can use it with VirtualMotionCapture or something like that, the VMC or OSC is still working.

I think it's possible to: open the VMC, close steam-vr, and link it with slime-vr.

# 描述

如果直接用oculus（而跳过steamvr）打开游戏，电脑性能会很好，但SlimeVR追踪器就没法用了。

这是可以直接用于oculus串流（非steamvr）游戏的SlimeVRFeederApp程序，使用C#编写（可以作为游戏Mod加载）。这样SlimeVR就不用依赖SteamVR了。

但是oculus不支持tracker，这个没法解决。这个插件能让SlimeVR知道头显和手柄的位置，但游戏不知道SlimeVR追踪器的位置。需要再做一些事情。

目前在节奏光剑中可用。

可以和VMC或类似的软件一起用，VMC或者OSC协议都能用。

感觉能做到打开VMC，关掉steam-vr，把它链接到slime-vr上。