const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('monitor', {
  state: () => ipcRenderer.invoke('state'),
  action: (name, value) => ipcRenderer.invoke('action', name, value),
  preferences: patch => ipcRenderer.invoke('preferences', patch),
  resize: delta => ipcRenderer.invoke('resize-widget', delta),
  subscribe: callback => { const listener = (_, state) => callback(state); ipcRenderer.on('state', listener); return () => ipcRenderer.removeListener('state', listener); }
});
