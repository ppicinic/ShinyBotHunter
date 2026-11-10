import json,os,re,sys
P={r['name']:int(r['url'].rstrip('/').split('/')[-1]) for r in json.load(open('pokemon.json'))['results']}
base={n:i for n,i in P.items() if i<=1025}
def load(f,sub):
    s=set()
    for l in open(f):
        l=l.strip()
        d,_,n=l.rpartition('/')
        if d==sub: s.add(n[:-4])
    return s
src={'bw':{'':load('bwani.txt',''),'f':load('bwani.txt','female'),'s':load('bwani.txt','shiny'),'sf':load('bwani.txt','shiny/female')},
     'sd':{'':load('showdown.txt',''),'f':load('showdown.txt','female'),'s':load('showdown.txt','shiny'),'sf':load('showdown.txt','shiny/female')}}
def keys(slug):
    out=[]
    if slug in P: out.append(str(P[slug]))
    # species prefix + suffix
    best=None
    for n,i in base.items():
        if slug==n or slug.startswith(n+'-'):
            if best is None or len(n)>len(best[0]): best=(n,i)
    if not best:
        t=slug.split('-')[0]
        c=[(n,i) for n,i in base.items() if n.split('-')[0]==t]
        if c and slug in P:
            return [str(P[slug]),'x'],c[0]
    if best:
        suf=slug[len(best[0]):]
        out.append(str(best[1])+suf)
        out.append(str(best[1]))  # species fallback (marked)
    return out,best
def gen(i):
    for g,m in enumerate([151,251,386,493,649,721,809,905,1025],1):
        if i<=m: return g
res={}
for d,k,ks in [('Default','',''),('Female_Default','f','f'),('Shiny','s','s'),('Female_Shiny','sf','sf')]:
    files=[f[:-4] for f in os.listdir('D:/Sprites/'+d)]
    stat={}
    miss=[]
    for slug in files:
        ks_,best=keys(slug)
        g=gen(best[1]) if best else 0
        exact=ks_[:-1] if best else ks_
        hit=None
        for s in ('bw','sd'):
            if any(x in src[s][k] for x in exact): hit=s;break
        both=[s for s in('bw','sd') if any(x in src[s][k] for x in exact)]
        key=(g, '+'.join(both) or 'none')
        stat[key]=stat.get(key,0)+1
        if not both: miss.append((g,slug))
    print('==',d,len(files))
    for key in sorted(stat): print('  gen',key[0],key[1],stat[key])
    res[d]=miss
json.dump(res,open('miss.json','w'))
for d in res:
    print(d,'missing',len(res[d]))
    print('  ',' '.join(s for g,s in sorted(res[d]))[:1800])
