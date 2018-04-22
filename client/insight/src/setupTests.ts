import * as Enzyme from 'enzyme';
import * as Adapter from 'enzyme-adapter-react-16';
Enzyme.configure({ adapter: new Adapter() });

class LocalStorageMock {
  store = {};

  clear() {
    this.store = {};
  }

  getItem(key: string) {
    return this.store[key] || null;
  }

  setItem(key: string, value: string) {
    this.store[key] = value.toString();
  }

  removeItem(key: string) {
    delete this.store[key];
  }
}

declare var global: {localStorage: LocalStorageMock};
global.localStorage = new LocalStorageMock();