import json,os,sys,urllib.request,concurrent.futures as cf
exec(open('cov.py').read().split("res={}")[0])
RAW='https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/'
PATH={'bw':'versions/generation-v/black-white/animated/','sd':'other/showdown/'}
SUB={'':'','f':'female/','s':'shiny/','sf':'shiny/female/'}
ROOT='D:/Sprites'; OUT=ROOT+'/Animated'
jobs=[];report={}
for d,k in [('Default',''),('Female_Default','f'),('Shiny','s'),('Female_Shiny','sf')]:
    os.makedirs(f'{OUT}/{d}',exist_ok=True)
    for f in os.listdir(f'{ROOT}/{d}'):
        slug=f[:-4]; ks_,best=keys(slug)
        if not best: report.setdefault('nomatch',[]).append(d+'/'+slug); continue
        g=gen(best[1]); exact=ks_[:-1]
        order=('bw','sd') if g<=5 else ('sd','bw')
        pick=None
        for s in order:
            for x in exact:
                if x in src[s][k]: pick=(s,k,x);break
            if pick:break
        if not pick and k in('f','sf') and slug!='eevee':
            # female-only species: the ordinary animation is the female one
            kk='' if k=='f' else 's'
            for s in order:
                for x in exact:
                    if x in src[s][kk]: pick=(s,kk,x);break
                if pick:break
        if not pick: report.setdefault('static',[]).append(d+'/'+slug); continue
        report[pick[0]]=report.get(pick[0],0)+1
        jobs.append((RAW+PATH[pick[0]]+SUB[pick[1]]+pick[2]+'.gif',f'{OUT}/{d}/{slug}.gif'))
def dl(j):
    u,p=j
    if os.path.exists(p): return 0
    for t in range(3):
        try:
            b=urllib.request.urlopen(u,timeout=30).read(); open(p,'wb').write(b); return len(b)
        except Exception as e: err=e
    return str(err)+' '+u
with cf.ThreadPoolExecutor(24) as ex: r=list(ex.map(dl,jobs))
fails=[x for x in r if isinstance(x,str)]
print('jobs',len(jobs),'bytes',sum(x for x in r if isinstance(x,int)),'fails',len(fails),fails[:5])
print({k:(v if isinstance(v,int) else len(v)) for k,v in report.items()})
json.dump(report,open('report.json','w'),indent=1)
