#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commctrl.h>
#include <string>
#include <vector>
#include <fstream>
#include <sstream>
#include <iomanip>
#include <locale>
#include <cmath>
#include <cstring>
#include <cstdint>
#include <array>

// RTSS client plugin: loaded by RTSS itself, never by a game or the battery app.
// Layout and offsets follow the public RTSS and Afterburner monitoring SDKs.
namespace {
const char* Owner="DS4Overlay.PCStats";
#ifdef DS4_PC_TEST
const wchar_t* RtssMap=L"DS4PCStats.QA.RTSS";
const wchar_t* MonitoringMap=L"DS4PCStats.QA.MAHM";
#else
const wchar_t* RtssMap=L"RTSSSharedMemoryV2";
const wchar_t* MonitoringMap=L"MAHMSharedMemory";
#endif
const wchar_t* ClassName=L"DS4RTSSPCStatsClient";
HINSTANCE module;
HWND window;
bool shown=true;
bool configuring=false;
bool mirrorAfterburner=false;
const char* SourceMarker="<P=100000,100000><L248>";
DWORD nextRender=0, nextConfig=0;
enum Action { ToggleAction, ShowAction, HideAction };
struct ShortcutBinding { DWORD key=0, lastPress=0; bool held=false, havePress=false; };
std::array<ShortcutBinding,3> shortcuts;
const wchar_t* ShortcutNames[]={L"ToggleHotkey",L"ShowHotkey",L"HideHotkey"};
std::string status="Not started";
struct Binding { std::string name; int decimals; bool comma; };
std::string pattern;
std::vector<Binding> bindings;

struct Mapping {
    HANDLE handle=nullptr; BYTE* memory=nullptr; size_t capacity=0;
    ~Mapping(){Close();}
    void Close(){if(memory)UnmapViewOfFile(memory);if(handle)CloseHandle(handle);memory=nullptr;handle=nullptr;capacity=0;}
    bool Open(const wchar_t* name, bool write){
        if(memory)return true;
        DWORD access=write?FILE_MAP_ALL_ACCESS:FILE_MAP_READ;
        handle=OpenFileMappingW(access,FALSE,name);if(!handle)return false;
        memory=(BYTE*)MapViewOfFile(handle,access,0,0,0);
        MEMORY_BASIC_INFORMATION region{};
        if(!memory || !VirtualQuery(memory,&region,sizeof(region))){Close();return false;}
        capacity=region.RegionSize;return true;
    }
    bool Range(uint64_t at,uint64_t length)const{return at<=capacity && length<=capacity-at;}
    uint32_t Read(size_t at)const{uint32_t v;memcpy(&v,memory+at,4);return v;}
};
Mapping rtss, monitoring;
int slot=-1;
std::string previous;
std::vector<BYTE> previousObjects;

std::wstring DataPath(){wchar_t path[MAX_PATH];DWORD size=GetModuleFileNameW(module,path,MAX_PATH);if(!size||size>=MAX_PATH)return L"";std::wstring p(path,size);return p.substr(0,p.find_last_of(L'.'))+L".dat";}
std::wstring ConfigPath(){std::wstring path=DataPath();return path.substr(0,path.size()-4)+L".cfg";}
DWORD Alternate(DWORD key){
    DWORD vk=key&0xffff,alternate=vk;
    if(key&0x80000)return key;
    const DWORD numpad[]={VK_INSERT,VK_END,VK_DOWN,VK_NEXT,VK_LEFT,VK_CLEAR,VK_RIGHT,VK_HOME,VK_UP,VK_PRIOR,VK_DELETE};
    if(vk>=VK_NUMPAD0&&vk<=VK_NUMPAD9)alternate=numpad[vk-VK_NUMPAD0];
    else if(vk==VK_DECIMAL)alternate=VK_DELETE;
    else for(DWORD i=0;i<11;i++)if(vk==numpad[i]){alternate=i==10?VK_DECIMAL:VK_NUMPAD0+i;break;}
    return (key&0xffff0000)|alternate;
}
UINT Modifiers(DWORD key){DWORD flags=(key>>16)&7;return ((flags&HOTKEYF_CONTROL)?MOD_CONTROL:0)|((flags&HOTKEYF_SHIFT)?MOD_SHIFT:0)|((flags&HOTKEYF_ALT)?MOD_ALT:0);}
bool ModifiersMatch(DWORD key,bool control,bool shift,bool alt,bool win){DWORD flags=(key>>16)&7;return !win&&control==!!(flags&HOTKEYF_CONTROL)&&shift==!!(flags&HOTKEYF_SHIFT)&&alt==!!(flags&HOTKEYF_ALT);}
DWORD ReadShortcut(const std::wstring& path,const wchar_t* name=L"ToggleHotkey"){wchar_t text[32];GetPrivateProfileStringW(L"Settings",name,L"0",text,32,path.c_str());wchar_t* end=nullptr;unsigned long value=wcstoul(text,&end,16);return end!=text&&(*end==0||((*end==L'h'||*end==L'H')&&end[1]==0))&&((value&0xffff)<=255)&&(value&0xfff00000)==0?value:0;}
std::array<DWORD,3> ReadShortcuts(const std::wstring& path){return {ReadShortcut(path,ShortcutNames[0]),ReadShortcut(path,ShortcutNames[1]),ReadShortcut(path,ShortcutNames[2])};}
std::array<DWORD,3> CurrentShortcuts(){return {shortcuts[0].key,shortcuts[1].key,shortcuts[2].key};}
bool DistinctShortcuts(const std::array<DWORD,3>& keys){
    for(size_t i=0;i<keys.size();i++)for(size_t j=0;j<i;j++)
        if((keys[i]&255)&&(keys[j]&255)&&(keys[i]==keys[j]||Alternate(keys[i])==keys[j]||keys[i]==Alternate(keys[j])))return false;
    return true;
}
void UnbindShortcuts(){if(window)for(int id=501;id<=506;id++)UnregisterHotKey(window,id);}
void BindShortcuts(const std::array<DWORD,3>& keys){
    UnbindShortcuts();
    for(size_t i=0;i<keys.size();i++){
        shortcuts[i]=ShortcutBinding{};shortcuts[i].key=keys[i];DWORD key=keys[i];
        if(window&&(key&0xffff)){
            RegisterHotKey(window,501+(int)i*2,MOD_NOREPEAT|Modifiers(key),key&0xffff);
            DWORD other=Alternate(key);if(other!=key)RegisterHotKey(window,502+(int)i*2,MOD_NOREPEAT|Modifiers(key),other&0xffff);
        }
    }
}
bool SaveShortcuts(const std::wstring& path,const std::array<DWORD,3>& keys){
    if(!DistinctShortcuts(keys))return false;
    wchar_t temporary[MAX_PATH];std::wstring directory=path.substr(0,path.find_last_of(L"\\/")+1);
    if(!GetTempFileNameW(directory.c_str(),L"ds4",0,temporary))return false;
    bool good=true;
    if(GetFileAttributesW(path.c_str())!=INVALID_FILE_ATTRIBUTES)good=CopyFileW(path.c_str(),temporary,FALSE)!=FALSE;
    for(size_t i=0;i<keys.size()&&good;i++){wchar_t value[24];swprintf_s(value,L"%08X",keys[i]);good=WritePrivateProfileStringW(L"Settings",ShortcutNames[i],value,temporary)!=FALSE;}
    if(good){WritePrivateProfileStringW(nullptr,nullptr,nullptr,temporary);good=MoveFileExW(temporary,path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)!=FALSE;}
    if(!good)DeleteFileW(temporary);return good;
}
bool ReadInt(std::istream& in,uint32_t& value){return !!in.read((char*)&value,4);}
bool ReadString(std::istream& in,std::string& value,uint32_t limit){uint32_t size;if(!ReadInt(in,size)||size>limit)return false;value.resize(size);return size==0||!!in.read(&value[0],size);}
bool Load(){
    std::ifstream file(DataPath(),std::ios::binary);
    char magic[8];uint32_t count;
    if(!file.read(magic,8)||memcmp(magic,"DS4PC001",8)||!ReadInt(file,count)||count==0||count>1000||!ReadString(file,pattern,30000))return false;
    bindings.clear();
    for(uint32_t i=0;i<count;i++){Binding b;uint32_t decimals,comma;
        if(!ReadString(file,b.name,259)||b.name.empty()||!ReadInt(file,decimals)||decimals>6||!ReadInt(file,comma)||comma>1)return false;
        b.decimals=(int)decimals;b.comma=comma!=0;bindings.push_back(b);
    }
    return file.peek()==std::char_traits<char>::eof();
}
bool TextEquals(const BYTE* value,size_t size,const char* expected){size_t n=strlen(expected);return n<size && !memcmp(value,expected,n) && value[n]==0;}
bool RtssLayout(uint32_t& size,uint32_t& offset,uint32_t& count){
    if(!rtss.Open(RtssMap,true))return false;
    if(!rtss.Range(0,40)||rtss.Read(0)!=0x52545353 || rtss.Read(4)<0x2000e || rtss.Read(4)>=0x30000){rtss.Close();slot=-1;return false;}
    size=rtss.Read(20);offset=rtss.Read(24);count=rtss.Read(28);
    uint32_t required=rtss.Read(4)>=0x20014?299520:4608;
    if(size<required||size>1048576||offset<40||count<2||count>256||!rtss.Range(offset,(uint64_t)size*count)){rtss.Close();slot=-1;return false;}
    return true;
}
struct RenderLock {
    volatile LONG* busy; bool locked;
    explicit RenderLock(BYTE* memory):busy((volatile LONG*)(memory+36)),locked(InterlockedCompareExchange(busy,1,0)==0){}
    ~RenderLock(){if(locked)InterlockedExchange(busy,0);}
};
bool Write(const std::string& text,const std::vector<BYTE>* objects=nullptr){
    uint32_t size,offset,count;if(!RtssLayout(size,offset,count)){status="Waiting for RTSS";return false;}
    RenderLock lock(rtss.memory);if(!lock.locked)return false;
    auto at=[&](uint32_t i){return rtss.memory+offset+(size_t)size*i;};
    if(slot<1||(uint32_t)slot>=count||!TextEquals(at(slot)+256,256,Owner)){
        slot=-1;
        for(uint32_t i=1;i<count;i++)if(TextEquals(at(i)+256,256,Owner)){slot=(int)i;break;}
        if(slot<0)for(uint32_t i=1;i<count;i++)if(at(i)[256]==0){slot=(int)i;memcpy(at(i)+256,Owner,strlen(Owner)+1);break;}
        previous.clear();previousObjects.clear();
    }
    if(slot<0){status="RTSS has no free display slot";return false;}
    bool modern=rtss.Read(4)>=0x20014;size_t max=modern?32768:4096;
    if(text.size()>=max||(objects&&objects->size()>262144)){status="Stats layout is too large";return false;}
    BYTE* entry=at(slot);BYTE* destination=entry+(modern?266752:512);
    bool changedObjects=objects?previousObjects!=*objects:!previousObjects.empty();
    if(previous==text && TextEquals(destination,max,text.c_str())&&!changedObjects)return true;
    entry[0]=0;entry[512]=0;
    memcpy(destination,text.c_str(),text.size()+1);
    if(changedObjects){
        if(objects&&!objects->empty())memcpy(entry+4608,objects->data(),objects->size());
        else if(!previousObjects.empty())memset(entry+4608,0,previousObjects.size());
        previousObjects=objects?*objects:std::vector<BYTE>{};
    }
    InterlockedIncrement((volatile LONG*)(rtss.memory+32));previous=text;return true;
}
void Release(){
    uint32_t size,offset,count;
    if(!rtss.memory||!RtssLayout(size,offset,count))return;
    RenderLock lock(rtss.memory);if(!lock.locked)return;
    for(uint32_t i=1;i<count;i++){
        BYTE* entry=rtss.memory+offset+(size_t)size*i;
        if(!TextEquals(entry+256,256,Owner))continue;
        entry[0]=entry[512]=entry[256]=0;if(size>=299520)entry[266752]=0;
        InterlockedIncrement((volatile LONG*)(rtss.memory+32));
    }
    slot=-1;previous.clear();previousObjects.clear();
}
enum class MirrorResult { Ready, Waiting, Unredirected, Busy, Invalid };
MirrorResult ReadAfterburner(std::string& text,std::vector<BYTE>& objects){
    text.clear();objects.clear();
    uint32_t size,offset,count;if(!RtssLayout(size,offset,count))return MirrorResult::Waiting;
    RenderLock lock(rtss.memory);if(!lock.locked)return MirrorResult::Busy;
    for(uint32_t i=0;i<count;i++){
        const BYTE* entry=rtss.memory+offset+(size_t)size*i;
        if(!TextEquals(entry+256,256,"MSIAfterburner"))continue;
        auto read=[](const BYTE* data,size_t capacity,std::string& out){const BYTE* end=(const BYTE*)memchr(data,0,capacity);if(!end)return false;out.assign((const char*)data,(const char*)end);return true;};
        std::string source;
        if(rtss.Read(4)>=0x20014&&!read(entry+266752,32768,source))return MirrorResult::Invalid;
        if(source.empty()&&!read(entry+512,4096,source))return MirrorResult::Invalid;
        if(source.empty()&&!read(entry,256,source))return MirrorResult::Invalid;
        if(source.empty())return MirrorResult::Ready;
        // Only mirror output deliberately redirected by our integration setup.
        // An unmarked Afterburner client continues to display its own output.
        size_t marker=strlen(SourceMarker);
        if(source.compare(0,marker,SourceMarker)!=0)return MirrorResult::Unredirected;
        source.erase(0,marker);
        if(source.empty())return MirrorResult::Ready;
        text="<P=0,0><L0>"+source+"<P=0,0><L246>\r";
        size_t max=rtss.Read(4)>=0x20014?32768:4096;
        if(text.size()>=max){text.clear();return MirrorResult::Invalid;}
        if(source.find("<OBJ")!=std::string::npos)objects.assign(entry+4608,entry+4608+262144);
        return MirrorResult::Ready;
    }
    return MirrorResult::Waiting;
}
bool Render(std::string& result){
    if(!monitoring.Open(MonitoringMap,false)){status="Start MSI Afterburner for live readings";return false;}
    if(!monitoring.Range(0,32)||monitoring.Read(0)!=0x4d41484d||monitoring.Read(4)<0x20000){monitoring.Close();status="Waiting for Afterburner monitoring";return false;}
    uint32_t header=monitoring.Read(8),count=monitoring.Read(12),size=monitoring.Read(16);
    if(header<32||count>4096||size<1324||size>16384||!monitoring.Range(header,(uint64_t)size*count)){monitoring.Close();status="Unsupported monitoring layout";return false;}
    uint32_t stamp=monitoring.Read(20);result=pattern;
    for(size_t i=0;i<bindings.size();i++){
        const Binding& b=bindings[i];std::string value="--";
        for(uint32_t j=0;j<count;j++){
            const BYTE* entry=monitoring.memory+header+(size_t)size*j;
            if(!TextEquals(entry,260,b.name.c_str()))continue;
            float sample;memcpy(&sample,entry+1300,4);
            if(std::isfinite(sample)&&sample!=3.402823466e+38F){std::ostringstream output;output.imbue(std::locale::classic());output<<std::fixed<<std::setprecision(b.decimals)<<sample;value=output.str();if(b.comma)for(char& c:value)if(c=='.')c=',';}
            break;
        }
        std::string token="{{DS4_PC_"+std::to_string(i)+"}}";size_t at=0;
        while((at=result.find(token,at))!=std::string::npos){result.replace(at,token.size(),value);at+=value.size();}
    }
    if(monitoring.Read(20)!=stamp)return false; // Keep the previous complete frame during an update.
    return true;
}
void Update(){
    std::string text;
    if(!shown){Write("");status="PC stats hidden";return;}
    if(mirrorAfterburner){
        std::vector<BYTE> objects;auto result=ReadAfterburner(text,objects);
        if(result==MirrorResult::Busy)return;
        if(result!=MirrorResult::Ready){
            Write("");
            status=result==MirrorResult::Unredirected?"Afterburner is displaying its own output; live-layout setup is required":result==MirrorResult::Invalid?"Afterburner supplied an unsupported or oversized layout":"Waiting for MSI Afterburner - select monitoring items and click Apply";
            return;
        }
        if(Write(text,&objects))status=text.empty()?"No Afterburner OSD items selected":"PC stats visible - following MSI Afterburner";
        return;
    }
    if(!Render(text)){if(status!="PC stats visible")Write("");return;}
    if(Write(text))status="PC stats visible";
}
void InvokeAction(size_t action,DWORD now){
    if(action>=shortcuts.size())return;auto& binding=shortcuts[action];
    if(binding.havePress&&(DWORD)(now-binding.lastPress)<100)return;
    binding.havePress=true;binding.lastPress=now;
    bool desired=action==ToggleAction?!shown:action==ShowAction;
    if(desired!=shown){shown=desired;Update();}
}
void Toggle(){InvokeAction(ToggleAction,GetTickCount());}
LRESULT CALLBACK Procedure(HWND hwnd,UINT message,WPARAM w,LPARAM l){
    if(message==WM_HOTKEY && w>=501&&w<=506){if(!configuring)InvokeAction((w-501)/2,GetTickCount());return 0;}
    if(message==WM_TIMER){
        DWORD now=GetTickCount();
        if(!configuring){
            if((LONG)(now-nextConfig)>=0){nextConfig=now+1000;auto keys=ReadShortcuts(ConfigPath());if(keys!=CurrentShortcuts()&&DistinctShortcuts(keys))BindShortcuts(keys);}
            for(size_t i=0;i<shortcuts.size();i++){
                auto& binding=shortcuts[i];DWORD key=binding.key;
                bool down=(key&0xffff)&&(GetAsyncKeyState(key&0xffff)<0||GetAsyncKeyState(Alternate(key)&0xffff)<0);
                bool matches=ModifiersMatch(key,GetAsyncKeyState(VK_CONTROL)<0,GetAsyncKeyState(VK_SHIFT)<0,GetAsyncKeyState(VK_MENU)<0,GetAsyncKeyState(VK_LWIN)<0||GetAsyncKeyState(VK_RWIN)<0);
                if(down&&!binding.held&&matches)InvokeAction(i,now);binding.held=down;
            }
        }
        if((LONG)(now-nextRender)>=0){nextRender=now+500;Update();}return 0;
    }
    return DefWindowProcW(hwnd,message,w,l);
}
}

extern "C" __declspec(dllexport) BOOL Start(){
    if(window)return TRUE;
    try{
        mirrorAfterburner=GetPrivateProfileIntW(L"Settings",L"MirrorAfterburner",0,ConfigPath().c_str())!=0;
        if(!mirrorAfterburner&&!Load()){status="Missing or invalid DS4PCStats.dat layout";return FALSE;}
        WNDCLASSW wc{};wc.hInstance=module;wc.lpszClassName=ClassName;wc.lpfnWndProc=Procedure;
        if(!RegisterClassW(&wc)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return FALSE;
        window=CreateWindowExW(0,ClassName,L"DS4 PC stats RTSS client",WS_POPUP,0,0,0,0,nullptr,nullptr,module,nullptr);
        if(!window)return FALSE;
        auto keys=ReadShortcuts(ConfigPath());if(!DistinctShortcuts(keys)){status="Choose different shortcuts for Toggle, Show and Hide";DestroyWindow(window);window=nullptr;return FALSE;}
        BindShortcuts(keys);
        if(!SetTimer(window,1,20,nullptr)){UnbindShortcuts();DestroyWindow(window);window=nullptr;return FALSE;}
        shown=true;nextRender=GetTickCount()+500;Update();return TRUE;
    }catch(...){status="Could not start the RTSS stats plugin";return FALSE;}
}
extern "C" __declspec(dllexport) void Stop(){
    if(window){KillTimer(window,1);UnbindShortcuts();DestroyWindow(window);window=nullptr;}
    for(int i=0;i<10;i++){Release();if(slot<0)break;Sleep(1);}monitoring.Close();rtss.Close();bindings.clear();
}
INT_PTR CALLBACK SettingsProcedure(HWND dialog,UINT message,WPARAM w,LPARAM){
    if(message==WM_INITDIALOG){
        HFONT font=(HFONT)GetStockObject(DEFAULT_GUI_FONT);
        auto control=[&](const wchar_t* cls,const wchar_t* text,DWORD style,int x,int y,int width,int height,int id){HWND item=CreateWindowExW(0,cls,text,WS_CHILD|WS_VISIBLE|style,x,y,width,height,dialog,(HMENU)(INT_PTR)id,module,nullptr);SendMessageW(item,WM_SETFONT,(WPARAM)font,TRUE);return item;};
        const wchar_t* labels[]={L"Toggle PC stats",L"Show PC stats",L"Hide PC stats"};
        for(int i=0;i<3;i++){
            control(L"STATIC",labels[i],0,18,22+i*44,136,22,10+i*2);
            HWND field=control(HOTKEY_CLASSW,L"",WS_TABSTOP|WS_BORDER,160,16+i*44,228,30,11+i*2);
            DWORD key=shortcuts[i].key;SendMessageW(field,HKM_SETHOTKEY,(key&255)|(((key>>16)&15)<<8),0);
        }
        control(L"STATIC",L"Click a field and press your shortcut. Backspace clears it.\n\nChoose monitoring items and customize the layout in MSI Afterburner, then click Apply.\n\nLeave Afterburner's global OSD shortcuts unassigned. These shortcuts control only PC stats; the battery keeps its own.",0,18,150,370,116,20);
        control(L"BUTTON",L"Save",WS_TABSTOP|BS_DEFPUSHBUTTON,210,274,86,30,IDOK);
        control(L"BUTTON",L"Cancel",WS_TABSTOP,302,274,86,30,IDCANCEL);
        SetFocus(GetDlgItem(dialog,11));return FALSE;
    }
    if(message==WM_COMMAND){
        if(LOWORD(w)==IDCANCEL){EndDialog(dialog,IDCANCEL);return TRUE;}
        if(LOWORD(w)==IDOK){
            std::array<DWORD,3> keys{};
            for(int i=0;i<3;i++){DWORD captured=(DWORD)SendDlgItemMessageW(dialog,11+i*2,HKM_GETHOTKEY,0,0);keys[i]=(captured&255)|(((captured>>8)&15)<<16);if((keys[i]&255)==VK_BACK)keys[i]=0;}
            if(!DistinctShortcuts(keys)){MessageBoxW(dialog,L"Choose different shortcuts for Toggle, Show and Hide.",L"PC stats",MB_OK|MB_ICONINFORMATION);return TRUE;}
            if(!SaveShortcuts(ConfigPath(),keys)){MessageBoxW(dialog,L"Could not save the shortcuts in RTSS's plugin folder. Your previous settings are unchanged.",L"PC stats",MB_OK|MB_ICONWARNING);return TRUE;}
            BindShortcuts(keys);EndDialog(dialog,IDOK);return TRUE;
        }
    }
    return FALSE;
}
extern "C" __declspec(dllexport) BOOL Setup(HWND parent){
    if(parent){
        if(!window)BindShortcuts(ReadShortcuts(ConfigPath()));
        INITCOMMONCONTROLSEX common{sizeof(common),ICC_HOTKEY_CLASS};InitCommonControlsEx(&common);
        configuring=true;UnbindShortcuts();
        struct Dialog { DLGTEMPLATE item; WORD menu,cls; wchar_t title[34]; } data{};
        data.item.style=WS_POPUP|WS_CAPTION|WS_SYSMENU|DS_MODALFRAME|DS_CENTER;data.item.cx=276;data.item.cy=214;
        wcscpy_s(data.title,L"DS4 PC stats - RTSS plugin");
        INT_PTR result=DialogBoxIndirectParamW(module,&data.item,parent,SettingsProcedure,0);
        DWORD error=result==-1?GetLastError():0;
        BindShortcuts(CurrentShortcuts());configuring=false;
        if(result==-1){SetLastError(error);return FALSE;}
    }
    return TRUE;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
