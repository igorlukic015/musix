using NAudio.CoreAudioApi;

MMDeviceEnumerator enumerator = new();
MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
Console.WriteLine(device.FriendlyName);
