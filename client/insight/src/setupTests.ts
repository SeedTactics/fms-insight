import * as Enzyme from 'enzyme';
import * as Adapter from 'enzyme-adapter-react-16';
Enzyme.configure({ adapter: new Adapter() });

declare var process: {
   env: {
       TZ: string
   }
};
process.env.TZ = 'America/Chicago';