import { check } from 'k6';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import { defaultHandleSummary, withSummaryTrendStats } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { diagnosticBreakdownThresholds } from '../../../ngb-performance-tests-framework/src/profiles/thresholds.ts';
import { resolvePeriodProfile } from '../flows/pmFlowSupport.ts';
const cases = [
 ...['pm.property','pm.party'].flatMap(type => [true,false].map(total => ({
  name:`${type}.${total ? 'total' : 'no_total'}`,path:`/api/catalogs/${type}`, query:{offset:0,limit:20,includeTotal:total},
 }))),
 ...[true,false].flatMap(total => [0,1].map(offset => ({
  name:`maintenance.${total ? 'total' : 'no_total'}.offset${offset}`,path:'/api/documents/pm.maintenance_request',
  query:{offset,limit:20,includeTotal:total,'deleted':'active'},
 }))),
 {name:'maintenance.period',path:'/api/documents/pm.maintenance_request',query:{offset:0,limit:20,
  'deleted':'active','periodFrom':resolvePeriodProfile().fromUtc,'periodTo':resolvePeriodProfile().toUtc}},
];
export const options = withSummaryTrendStats({
 scenarios:{read_probe:{executor:'per-vu-iterations',vus:1,iterations:6,maxDuration:'3m'}},
 thresholds:{checks:['rate==1'],http_req_failed:['rate==0'], ...diagnosticBreakdownThresholds(cases.map(c=>({operation:c.name})))},
});
export function setup():NgbAuthSetupData {return setupNgbAccessToken();}
export default function(data:NgbAuthSetupData):void {
 const c=getNgbScenarioContext(data);
 for(const probe of cases){
  const r=c.http.get(probe.path,{query:probe.query,tags:{operation:probe.name},expectedStatuses:[200]});
  let body:Record<string,unknown>={};try{body=r.json() as Record<string,unknown>;}catch{}
  check(r,{'read probe returned page':()=>r.status===200 && Array.isArray(body.items)});
  console.log(JSON.stringify({operation:probe.name,iteration:__ITER,status:r.status,durationMs:r.timings.duration,
   items:Array.isArray(body.items)?body.items.length:null,total:body.total,hasMore:body.hasMore}));
 }
}
export function handleSummary(data:unknown):Record<string,string>{return defaultHandleSummary(data);}
