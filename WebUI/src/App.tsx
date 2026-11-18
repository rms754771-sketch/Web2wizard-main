import { FormEvent, ReactNode, useEffect, useMemo, useState } from 'react'

type BuildRecord = { name: string; url: string; version: string; status: string; installer: string; time: string }
type HostState = { connected: boolean; builds: BuildRecord[]; outputDirectory: string; tools: { dotnet: boolean; webView2: boolean }; building: boolean }
declare global { interface Window { chrome?: { webview?: { postMessage: (message: unknown) => void; addEventListener: (name: string, callback: (event: MessageEvent) => void) => void } } } }
const sendHostMessage = (type: string, data: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ type, ...data })

type View = 'dashboard' | 'create' | 'history' | 'settings' | 'about'
type IconName =
  | 'home'
  | 'plus'
  | 'history'
  | 'settings'
  | 'info'
  | 'apps'
  | 'check'
  | 'package'
  | 'external'
  | 'folder'
  | 'chevron'
  | 'globe'
  | 'clock'
  | 'close'
  | 'menu'
  | 'refresh'
  | 'shield'
  | 'code'

const paths: Record<IconName, ReactNode> = {
  home: <><path d="m3 10 9-7 9 7" /><path d="M5 9v11h14V9M9 20v-7h6v7" /></>,
  plus: <><path d="M12 5v14M5 12h14" /></>,
  history: <><path d="M3 12a9 9 0 1 0 3-6.7L3 8" /><path d="M3 3v5h5M12 7v5l3 2" /></>,
  settings: <><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1a1.7 1.7 0 0 0 1.9.3A1.7 1.7 0 0 0 10 3v-.2h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z" /></>,
  info: <><circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7h.01" /></>,
  apps: <><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><rect x="14" y="14" width="7" height="7" rx="1" /></>,
  check: <path d="m5 12 4 4L19 6" />,
  package: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 8 9 5 9-5v9l-9 5-9-5V8ZM12 13v9" /></>,
  external: <><path d="M14 4h6v6M20 4l-9 9" /><path d="M18 13v6a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h6" /></>,
  folder: <path d="M3 6h7l2 2h9v10a2 2 0 0 1-2 2H3V6Z" />,
  chevron: <path d="m9 18 6-6-6-6" />,
  globe: <><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3c3 3.5 3 14 0 18M12 3c-3 3.5-3 14 0 18" /></>,
  clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
  close: <path d="m6 6 12 12M18 6 6 18" />,
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  refresh: <><path d="M20 7v5h-5" /><path d="M19 12a7 7 0 1 0-2 5" /></>,
  shield: <><path d="M12 3 5 6v5c0 4.5 2.8 8.4 7 10 4.2-1.6 7-5.5 7-10V6l-7-3Z" /><path d="m9 12 2 2 4-4" /></>,
  code: <><path d="m8 9-3 3 3 3M16 9l3 3-3 3M14 6l-4 12" /></>,
}

function Icon({ name, size = 18, className = '' }: { name: IconName; size?: number; className?: string }) {
  return <svg className={className} width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

const nav: { id: View; label: string; icon: IconName }[] = [
  { id: 'dashboard', label: 'Dashboard', icon: 'home' },
  { id: 'create', label: 'Create Application', icon: 'plus' },
  { id: 'history', label: 'Build History', icon: 'history' },
  { id: 'settings', label: 'Settings', icon: 'settings' },
  { id: 'about', label: 'About', icon: 'info' },
]

const builds: BuildRecord[] = []

function AppMark({ small = false }: { small?: boolean }) {
  return <div className={`app-mark ${small ? 'small' : ''}`}><span></span><span></span><span></span><span></span></div>
}

function Header({ view, connected, openMenu }: { view: View; connected: boolean; openMenu: () => void }) {
  const titles: Record<View, [string, string]> = { dashboard: ['Dashboard', 'Overview of your Web2EXE projects and recent activity.'], create: ['Create Application', 'Package a website as a Windows desktop application.'], history: ['Build History', 'Review your applications, builds, and installer status.'], settings: ['Settings', 'Configure local build tools and output preferences.'], about: ['About', 'Product information and application details.'] }
  return <header className="page-header"><button className="menu-button" onClick={openMenu} aria-label="Open navigation"><Icon name="menu" size={20} /></button><div><h1>{titles[view][0]}</h1><p>{titles[view][1]}</p></div><div className="header-status"><span style={{ background: connected ? '#36c98b' : '#d9a441' }}></span>{connected ? 'Desktop services connected' : 'Connecting desktop services…'}</div></header>
}

function Sidebar({ view, setView, open, close }: { view: View; setView: (v: View) => void; open: boolean; close: () => void }) {
  return <>
    {open && <div className="sidebar-backdrop" onClick={close} />}
    <aside className={`sidebar ${open ? 'open' : ''}`}>
      <div className="brand"><AppMark /><div><strong>Web2EXE</strong><span>WIZARD</span></div></div>
      <button className="sidebar-close" onClick={close} aria-label="Close navigation"><Icon name="close" /></button>
      <nav>
        <p className="nav-label">WORKSPACE</p>
        {nav.slice(0, 3).map(item => <button key={item.id} className={view === item.id ? 'active' : ''} onClick={() => { setView(item.id); close() }}>
          <Icon name={item.icon} /><span>{item.label}</span>{view === item.id && <i />}
        </button>)}
        <p className="nav-label nav-second">SYSTEM</p>
        {nav.slice(3).map(item => <button key={item.id} className={view === item.id ? 'active' : ''} onClick={() => { setView(item.id); close() }}>
          <Icon name={item.icon} /><span>{item.label}</span>{view === item.id && <i />}
        </button>)}
      </nav>
      <div className="sidebar-footer"><div className="version-pill">v1.0.0</div><p>Windows 10 / 11</p></div>
    </aside>
  </>
}

function StatCard({ title, value, detail, icon, accent }: { title: string; value: string; detail: string; icon: IconName; accent: string }) {
  return <article className="stat-card">
    <div className={`stat-icon ${accent}`}><Icon name={icon} size={20} /></div>
    <div className="stat-title">{title}</div>
    <strong className="stat-value">{value}</strong>
    <div className="stat-detail"><span className={accent === 'red' ? 'negative' : ''}>{accent === 'red' ? '1 issue' : '+2 this month'}</span> {detail}</div>
  </article>
}

function Dashboard({ navigate, builds }: { navigate: (v: View) => void; builds: BuildRecord[] }) {
  const successful = builds.filter(b => b.status === 'Successful').length
  const installers = builds.filter(b => b.installer === 'Created').length
  return <div className="content">
    <section className="hero"><div><span className="eyebrow"><span></span> WINDOWS APPLICATION BUILDER</span><h2>Turn any website into a<br /><em>desktop experience.</em></h2><p>Create polished Windows applications and installers from your web projects, all from one streamlined workspace.</p><button className="primary large" onClick={() => navigate('create')}><Icon name="plus" size={19} /> Create New Application <Icon name="chevron" size={17} /></button></div><div className="hero-graphic" aria-hidden="true"><div className="orbit one"></div><div className="orbit two"></div><div className="browser-card"><div className="browser-bar"><i></i><i></i><i></i><span></span></div><div className="browser-body"><div className="mini-side"></div><div><b></b><span></span><span></span><span></span></div></div></div><div className="connector"><span></span><span></span><span></span></div><div className="exe-tile"><AppMark small /><b>.EXE</b></div></div></section>
    <section className="stats-grid"><StatCard title="Applications built" value={String(builds.length)} detail="Recorded builds" icon="apps" accent="blue" /><StatCard title="Successful builds" value={String(successful)} detail={builds.length ? Math.round(successful / builds.length * 100) + '% success rate' : 'No builds yet'} icon="check" accent="green" /><StatCard title="Installers created" value={String(installers)} detail="Ready to distribute" icon="package" accent="violet" /></section>
    <section className="panel activity-panel"><div className="panel-heading"><div><h3>Recent build activity</h3><p>Your latest application builds and installer results.</p></div><button className="text-button" onClick={() => navigate('history')}>View build history <Icon name="chevron" size={15} /></button></div><div className="activity-list">{builds.slice(0, 5).map((build, index) => <div className="activity-row" key={build.name + index}><div className="app-avatar blue"><Icon name="globe" /></div><div className="activity-main"><strong>{build.name}</strong><span>{build.url}</span></div><div className="activity-version"><span>VERSION</span>{build.version}</div><Status value={build.status} /><div className="activity-time"><Icon name="clock" size={14} />{build.time}</div><button className="icon-button" onClick={() => sendHostMessage('openFolder')} aria-label="Open output folder"><Icon name="external" size={16} /></button></div>)}{!builds.length && <div className="empty-state"><Icon name="apps" size={28} /><b>No applications built yet</b><p>Create your first Windows application to see it here.</p></div>}</div></section>
  </div>
}

function Status({ value }: { value: string }) {
  const good = value === 'Successful' || value === 'Created'
  return <span className={`status ${good ? 'success' : 'failed'}`}><i></i>{value}</span>
}

function CreateApplication() {
  const [name, setName] = useState('')
  const [url, setUrl] = useState('')
  const [touched, setTouched] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [serviceError, setServiceError] = useState(false)
  const urlState = useMemo(() => {
    if (!url) return 'empty'
    try {
      const parsed = new URL(url)
      return ['http:', 'https:'].includes(parsed.protocol) && parsed.hostname.includes('.') ? 'valid' : 'invalid'
    } catch { return 'invalid' }
  }, [url])
  const host = urlState === 'valid' ? new URL(url).hostname : ''
  useEffect(() => { const handler = (event: MessageEvent) => { if (event.data?.type === 'buildFinished') setSubmitting(false); if (event.data?.type === 'buildError') { setSubmitting(false); setServiceError(true) } }; window.chrome?.webview?.addEventListener('message', handler) }, [])

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setTouched(true)
    setServiceError(false)
    if (!name.trim() || urlState !== 'valid') return
    setSubmitting(true)
    sendHostMessage('build', { name: name.trim(), url: url.trim() })
  }

  return <div className="content narrow-content">
    <div className="step-track"><div className="step active"><span>1</span><div><b>Application details</b><small>Name and website URL</small></div></div><i></i><div className="step"><span>2</span><div><b>Build application</b><small>Run the native Windows build</small></div></div></div>
    <div className="create-grid">
      <form className="panel form-panel" onSubmit={submit}>
        <div className="panel-heading"><div><h3>Application details</h3><p>Enter the website you want to package for Windows.</p></div></div>
        <label>Application name<span>*</span></label>
        <div className={`input-wrap ${touched && !name.trim() ? 'invalid' : ''}`}><Icon name="apps" size={17} /><input value={name} onChange={e => setName(e.target.value)} placeholder="e.g. My Company Portal" /></div>
        {touched && !name.trim() ? <p className="field-message error">Enter an application name.</p> : <p className="field-message">This name appears in Windows and the installer.</p>}
        <label>Website URL<span>*</span></label>
        <div className={`input-wrap ${(touched || url) && urlState === 'invalid' ? 'invalid' : urlState === 'valid' ? 'valid' : ''}`}><Icon name="globe" size={17} /><input value={url} onChange={e => { setUrl(e.target.value); setServiceError(false) }} onBlur={() => setTouched(true)} placeholder="https://example.com" /><span className="input-state">{urlState === 'valid' && <Icon name="check" size={15} />}</span></div>
        {(touched || url) && urlState === 'invalid' ? <p className="field-message error">Enter a complete URL, including https://</p> : <p className="field-message">{urlState === 'valid' ? 'Website address looks valid.' : 'Secure HTTPS websites are recommended.'}</p>}
        <div className="form-divider"></div>
        <div className="service-note"><Icon name="info" size={17} /><div><b>Desktop service required</b><p>The existing C# build pipeline creates the portable ZIP and custom Windows installer. No output is simulated.</p></div></div>
        {serviceError && <div className="error-banner"><Icon name="close" size={16} /><div><b>Build service unavailable</b><p>Check the build status and make sure the required Windows build tools are installed.</p></div></div>}
        <button className="primary form-submit" disabled={submitting}>{submitting ? <><span className="spinner"></span> Checking services…</> : <><Icon name="package" size={18} /> Create Application</>}</button>
      </form>
      <aside className="panel preview-panel">
        <div className="preview-title"><span>LIVE PREVIEW</span><small>Windows application</small></div>
        <div className="window-preview">
          <div className="window-bar"><div className="favicon">{host ? <img src={`https://www.google.com/s2/favicons?domain=${host}&sz=64`} alt="" onError={e => { e.currentTarget.style.display = 'none' }} /> : <Icon name="globe" size={14} />}</div><span>{name || 'Your application'}</span><div className="window-actions"><i>—</i><i>□</i><i>×</i></div></div>
          <div className="window-url"><Icon name="shield" size={12} /><span>{urlState === 'valid' ? url : 'https://your-website.com'}</span><Icon name="refresh" size={12} /></div>
          <div className="preview-empty"><div className="preview-logo">{host ? <img src={`https://www.google.com/s2/favicons?domain=${host}&sz=128`} alt="Website favicon preview" /> : <Icon name="globe" size={28} />}</div><b>{name || 'Your application'}</b><p>{host || 'Your website will appear here'}</p></div>
        </div>
        <div className="preview-meta"><div><span>WINDOW TITLE</span><b>{name || 'Not set'}</b></div><div><span>FAVICON</span><b className={host ? 'ready' : ''}>{host ? 'Detected' : 'Waiting for URL'}</b></div></div>
      </aside>
    </div>
  </div>
}

function BuildHistory({ builds }: { builds: BuildRecord[] }) {
  const [query, setQuery] = useState('')
  const filtered = builds.filter(b => (b.name + b.url).toLowerCase().includes(query.toLowerCase()))
  return <div className="content"><section className="panel history-panel"><div className="history-toolbar"><div><h3>All builds</h3><p>{builds.length} build records</p></div><div className="search-wrap"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></svg><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search applications..." /></div></div><div className="table-scroll"><table><thead><tr><th>APPLICATION</th><th>VERSION</th><th>BUILD STATUS</th><th>INSTALLER</th><th>BUILT</th><th></th></tr></thead><tbody>{filtered.map((build, index) => <tr key={build.name + index}><td><div className="table-app"><div className="app-avatar blue"><Icon name="globe" size={17} /></div><div><strong>{build.name}</strong><span>{build.url}</span></div></div></td><td><span className="version-tag">v{build.version}</span></td><td><Status value={build.status} /></td><td><Status value={build.installer} /></td><td className="date-cell">{build.time}</td><td><button className="folder-button" onClick={() => sendHostMessage('openFolder')} title="Open output folder"><Icon name="folder" size={17} /> <span>Open folder</span></button></td></tr>)}</tbody></table></div>{!filtered.length && <div className="empty-state"><Icon name="history" size={28} /><b>{builds.length ? 'No builds found' : 'No build history yet'}</b><p>{builds.length ? 'Try a different search term.' : 'Your completed builds will appear here.'}</p></div>}</section></div>
}

function Settings({ state }: { state: HostState }) {
  const [directory, setDirectory] = useState(state.outputDirectory)
  const [saved, setSaved] = useState(false)
  useEffect(() => setDirectory(state.outputDirectory), [state.outputDirectory])
  return <div className="content settings-content"><section className="panel settings-panel">
    <div className="settings-section"><div className="settings-icon"><Icon name="folder" /></div><div className="settings-copy"><h3>Build output</h3><p>Choose where application builds and installer files will be saved.</p><label>Output directory</label><div className="directory-row"><div className="input-wrap"><Icon name="folder" size={17} /><input value={directory} onChange={e => { setDirectory(e.target.value); setSaved(false) }} /></div><button className="secondary" onClick={() => sendHostMessage('browseOutput')}>Browse</button></div></div></div>
    <div className="settings-divider"></div>
    <div className="settings-section"><div className="settings-icon"><Icon name="code" /></div><div className="settings-copy"><div className="tool-heading"><div><h3>Required build tools</h3><p>Live checks from the Windows desktop application.</p></div><button className="secondary" onClick={() => sendHostMessage('checkTools')}><Icon name="refresh" size={15} /> Check again</button></div><div className="tool-list"><Tool name=".NET SDK" detail="Required to compile applications and the installer" ready={state.tools.dotnet} /><Tool name="WebView2 Runtime" detail="Required to run generated web applications" ready={state.tools.webView2} /></div><div className="detection-note"><Icon name="info" size={16} /><span>Installer generation uses the built-in C# installer builder. Inno Setup is not required.</span></div></div></div>
  </section><div className="settings-footer"><button className="primary" onClick={() => { sendHostMessage('saveSettings', { outputDirectory: directory }); setSaved(true) }}><Icon name="check" size={17} /> Save preferences</button><span>{saved ? 'Preferences saved on this computer.' : 'Preferences are stored locally on this computer.'}</span></div></div>
}

function Tool({ name, detail, ready }: { name: string; detail: string; ready: boolean }) {
  return <div className="tool-row"><div className="tool-mark"><Icon name="code" size={17} /></div><div><b>{name}</b><span>{detail}</span></div><span className={'status ' + (ready ? 'success' : 'unknown')}><i></i>{ready ? 'Available' : 'Not detected'}</span></div>
}

function About() {
  return <div className="content about-content">
    <section className="panel about-card">
      <div className="about-top"><div className="about-logo"><AppMark /></div><div><h2>Web2EXE Wizard</h2><p>Website to Windows application builder</p><span>VERSION 1.0.0</span></div></div>
      <div className="about-body"><h3>Bring the web to the desktop.</h3><p>Web2EXE Wizard provides a focused workspace for turning websites into standalone Windows applications and creating distribution-ready installers.</p><p>This interface is designed to connect with your existing C# application services for compilation, packaging, and installer generation.</p></div>
      <div className="about-features"><div><Icon name="globe" /><span><b>Web powered</b><small>Built around WebView2</small></span></div><div><Icon name="shield" /><span><b>Windows native</b><small>Designed for Windows 10 & 11</small></span></div><div><Icon name="package" /><span><b>Installer ready</b><small>Custom C# installer builder</small></span></div></div>
      <div className="about-foot"><span>© 2026 Web2EXE Wizard</span><span>React interface · Native C# build services</span></div>
    </section>
  </div>
}

export default function App() {
  const [view, setView] = useState<View>('dashboard')
  const [menuOpen, setMenuOpen] = useState(false)
  const [hostState, setHostState] = useState<HostState>({ connected: false, builds: [], outputDirectory: '', tools: { dotnet: false, webView2: false }, building: false })
  useEffect(() => {
    const handler = (event: MessageEvent) => {
      const data = event.data
      if (data?.type === 'hostState') setHostState(data.state)
      if (data?.type === 'buildStarted' || data?.type === 'buildProgress') setHostState(s => ({ ...s, building: true }))
      if (data?.type === 'buildFinished') setHostState(s => ({ ...s, building: false }))
      if (data?.type === 'toolsUpdated') setHostState(s => ({ ...s, tools: data.tools }))
      if (data?.type === 'settingsSaved') setHostState(s => ({ ...s, outputDirectory: data.outputDirectory }))
    }
    window.chrome?.webview?.addEventListener('message', handler)
    sendHostMessage('ready')
  }, [])
  useEffect(() => { window.scrollTo({ top: 0, behavior: 'smooth' }) }, [view])
  return <div className="app-shell"><Sidebar view={view} setView={setView} open={menuOpen} close={() => setMenuOpen(false)} /><main><Header view={view} connected={hostState.connected} openMenu={() => setMenuOpen(true)} />
    {view === 'dashboard' && <Dashboard navigate={setView} builds={hostState.builds} />}
    {view === 'create' && <CreateApplication />}
    {view === 'history' && <BuildHistory builds={hostState.builds} />}
    {view === 'settings' && <Settings state={hostState} />}
    {view === 'about' && <About />}
  </main></div>
}

