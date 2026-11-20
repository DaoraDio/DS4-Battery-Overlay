#define DS4_PC_TEST
#include "../../src/RTSSPlugin/DS4PCStats.cpp"
#include <iostream>
#include <stdexcept>
void Check(bool good,const char* name){if(!good)throw std::runtime_error(name);std::cout<<"PASS "<<name<<"\n";}
int main(){try{
    Check(Alternate(VK_NUMPAD2)==VK_DOWN && Alternate(VK_CLEAR)==VK_NUMPAD5 && Alternate(0x80000|VK_DOWN)==(0x80000|VK_DOWN),"configurable numpad aliases and extended arrows");
    DWORD custom=VK_F10|(HOTKEYF_CONTROL<<16)|(HOTKEYF_ALT<<16);
    Check(Modifiers(custom)==(MOD_CONTROL|MOD_ALT) && ModifiersMatch(custom,true,false,true,false) && !ModifiersMatch(custom,true,true,true,false),"arbitrary shortcuts require the configured modifiers");
    wchar_t temp[MAX_PATH];GetTempPathW(MAX_PATH,temp);
    std::wstring cfg=std::wstring(temp)+L"DS4PCStats-QA-"+std::to_wstring(GetCurrentProcessId())+L".cfg";
    Check(WritePrivateProfileStringW(L"Settings",L"ToggleHotkey",L"00060079",cfg.c_str())&&ReadShortcut(cfg)==custom,"shortcut comes from configuration instead of a hardcoded key");
    Check(ReadShortcuts(cfg)==std::array<DWORD,3>{custom,0,0},"existing toggle settings migrate with Show and Hide unassigned");
    WritePrivateProfileStringW(L"Other",L"Keep",L"untouched",cfg.c_str());
    std::array<DWORD,3> assigned{custom,VK_F11,VK_F12};
    Check(SaveShortcuts(cfg,assigned)&&ReadShortcuts(cfg)==assigned,"all three shortcuts save and reload together");
    wchar_t kept[32];GetPrivateProfileStringW(L"Other",L"Keep",L"",kept,32,cfg.c_str());Check(!wcscmp(kept,L"untouched"),"saving preserves other settings");
    Check(!DistinctShortcuts({VK_NUMPAD5,VK_CLEAR,0})&&!SaveShortcuts(cfg,{VK_F10,VK_F10,0})&&ReadShortcuts(cfg)==assigned,"duplicate shortcuts and numpad aliases are rejected without changing settings");
    Check(SaveShortcuts(cfg,{0,0,0})&&ReadShortcuts(cfg)==std::array<DWORD,3>{0,0,0},"any action may be left unassigned");DeleteFileW(cfg.c_str());
    HANDLE r=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,128+299520*4,RtssMap);
    HANDLE m=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,32+1324,MonitoringMap);
    Check(r&&m,"isolated fake mappings");
    BYTE* rm=(BYTE*)MapViewOfFile(r,FILE_MAP_ALL_ACCESS,0,0,0);
    BYTE* mm=(BYTE*)MapViewOfFile(m,FILE_MAP_ALL_ACCESS,0,0,0);
    auto put=[](BYTE* p,size_t at,uint32_t v){memcpy(p+at,&v,4);};
    put(rm,0,0x52545353);put(rm,4,0x20014);put(rm,20,299520);put(rm,24,128);put(rm,28,4);
    strcpy_s((char*)rm+128+256,256,"MSIAfterburner");strcpy_s((char*)rm+128+266752,32768,"FOREIGN STATS");
    strcpy_s((char*)rm+128+299520+256,256,"DS4Overlay.RTSSTest");strcpy_s((char*)rm+128+299520+266752,32768,"BATTERY ARTWORK");
    std::vector<BYTE> foreign(rm+128,rm+128+299520*2);
    put(mm,0,0x4d41484d);put(mm,4,0x20000);put(mm,8,32);put(mm,12,1);put(mm,16,1324);
    strcpy_s((char*)mm+32,260,"GPU temperature");float value=47.25f;memcpy(mm+32+1300,&value,4);
    bindings={{"GPU temperature",1,false}};pattern="<C0=008040>GPU <A0>{{DS4_PC_0}}<A> C\n<APP>\r<FR> FPS <FT> ms";
    std::string text;Check(Render(text)&&text.find("47.2")!=std::string::npos&&text.find("\r<FR>")!=std::string::npos,"live readings and exact RTSS markup");
    Check(Write(text)&&slot==2,"dedicated PC-stats slot");uint32_t frame=rtss.Read(32);
    Check(Write(text)&&frame==rtss.Read(32),"unchanged readings avoid unnecessary updates");
    shown=true;shortcuts={};InvokeAction(ToggleAction,100);Check(!shown&&rm[128+299520*2+266752]==0,"PC-stat toggle hides only its own output");
    InvokeAction(ToggleAction,101);Check(!shown,"registered and polling hotkey duplicate suppressed");
    InvokeAction(ToggleAction,250);Check(shown&&rm[128+299520*2+266752]!=0,"second toggle key press restores stats");
    InvokeAction(HideAction,300);Check(!shown&&rm[128+299520*2+266752]==0,"Hide shortcut hides PC stats");frame=rtss.Read(32);
    InvokeAction(HideAction,500);Check(!shown&&rtss.Read(32)==frame,"repeated Hide stays hidden without unnecessary writes");
    InvokeAction(ShowAction,501);Check(shown&&rm[128+299520*2+266752]!=0,"Show works immediately after Hide");frame=rtss.Read(32);
    InvokeAction(ShowAction,800);Check(shown&&rtss.Read(32)==frame,"repeated Show stays visible without toggling");
    Check(!memcmp(foreign.data(),rm+128,foreign.size()),"battery and foreign display bytes preserved");
    value=std::nanf("");memcpy(mm+32+1300,&value,4);Check(Render(text)&&text.find("--")!=std::string::npos,"invalid readings show unavailable marker");
    put(mm,12,4097);Check(!Render(text),"invalid monitoring bounds rejected");put(mm,12,1);
    Release();Check(rm[128+299520*2+256]==0&&!memcmp(foreign.data(),rm+128,foreign.size()),"unload releases only the plugin slot");
    std::vector<BYTE> objects;
    Check(ReadAfterburner(text,objects)==MirrorResult::Unredirected,"unredirected Afterburner output is not duplicated");
    std::string body="<C0=008040>GPU 60 C\nFAN 1200 RPM <OBJ=0>\n<APP> <FR> FPS <FT> ms";
    auto sourceText=[&](const std::string& value){strcpy_s((char*)rm+128+266752,32768,value.c_str());};
    sourceText(std::string(SourceMarker)+body);
    for(size_t i=0;i<262144;i++)rm[128+4608+i]=(BYTE)(i%251);
    std::vector<BYTE> sourceBefore(rm+128,rm+128+299520);
    std::vector<BYTE> batteryBefore(rm+128+299520,rm+128+299520*2);
    put(rm,36,1);Check(ReadAfterburner(text,objects)==MirrorResult::Busy&&rtss.Read(36)==1,"live reader respects the RTSS render lock");put(rm,36,0);
    Check(ReadAfterburner(text,objects)==MirrorResult::Ready&&text=="<P=0,0><L0>"+body+"<P=0,0><L246>\r"&&objects.size()==262144,"live layout follows Afterburner text, formatting, macros and embedded objects");
    Check(Write(text,&objects)&&!memcmp(rm+128+299520*2+4608,rm+128+4608,262144),"graph buffer is copied with matching relative object offsets");
    frame=rtss.Read(32);Check(Write(text,&objects)&&frame==rtss.Read(32),"unchanged mirrored frames are not republished");
    Check(!memcmp(sourceBefore.data(),rm+128,sourceBefore.size())&&!memcmp(batteryBefore.data(),rm+128+299520,batteryBefore.size()),"mirroring never rewrites Afterburner or battery slots");
    rm[128+4608+123]^=0x44;
    Check(ReadAfterburner(text,objects)==MirrorResult::Ready&&Write(text,&objects)&&rtss.Read(32)!=frame&&rm[128+299520*2+4608+123]==rm[128+4608+123],"graph-only updates refresh even when text is unchanged");
    sourceText(std::string(SourceMarker)+"GPU 61 C\nPOWER 90 W");
    Check(ReadAfterburner(text,objects)==MirrorResult::Ready&&objects.empty()&&text.find("POWER 90 W")!=std::string::npos&&text.find("FAN")==std::string::npos&&Write(text,&objects)&&rm[128+299520*2+4608+123]==0,"added and removed monitoring items replace the previous layout and graph buffer");
    sourceText(SourceMarker);mirrorAfterburner=true;shown=true;Update();
    Check(rm[128+299520*2+266752]==0,"no selected monitoring items leaves no frozen display");
    sourceText("UNREDIRECTED AFTERBURNER");Update();
    Check(rm[128+299520*2+266752]==0&&!strcmp((char*)rm+128+266752,"UNREDIRECTED AFTERBURNER"),"missing integration marker falls back to Afterburner's own display without duplicates");
    mirrorAfterburner=false;Release();
    put(rm,28,257);Check(!Write("invalid"),"invalid RTSS bounds rejected");
    monitoring.Close();rtss.Close();UnmapViewOfFile(mm);UnmapViewOfFile(rm);CloseHandle(m);CloseHandle(r);
    return 0;
}catch(const std::exception& error){std::cerr<<"FAIL "<<error.what()<<"\n";return 1;}}
