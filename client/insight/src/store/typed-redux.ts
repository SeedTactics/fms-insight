import * as React from "react";
import * as redux from "redux";
import * as reactRedux from "react-redux";

// a variant of middleware which tracks input and output types of the actions
export type Dispatch<A> = (a: A) => void;
export type Middleware<A1, A2> = (dispatch: Dispatch<A2>) => (a: A1) => void;
export function composeMiddleware<A1, A2, A3>(m1: Middleware<A1, A2>, m2: Middleware<A2, A3>): Middleware<A1, A3> {
  return (d) => m1(m2(d));
}

// Extract the state and action types from the type of an object of reducer functions.
// For example, Reducers will be the type of an object literal such as
// {
//    foo: fooReducer,
//    bar: barReducer
// }
// where fooReducer has type (s: FooState, a: FooAction) => FooState
// and   barReducer has type (s: BarState, a: BarAction) => BarState.
// ReducerFnsToState will translate this to a type
// {
//    foo: FooState,
//    bar: BarState
// }
// ReducerFnsToActions will translate this to a union type FooAction | BarAction
type ReducerFnsToState<Reducers> = {
  readonly [K in keyof Reducers]: Reducers[K] extends (s: infer S, a: infer A) => infer S
    ? S extends infer S2 | undefined
      ? S2
      : S
    : never;
};
type ReducerFnsToActions<Reducers> = {
  [K in keyof Reducers]: Reducers[K] extends (s: infer S, a: infer A) => infer S ? A : never;
}[keyof Reducers];

// A typed version of react-redux's connect which requires each action creator function to produce
// an action of the correct type.  It also extracts the arguments to each action creator and
// makes sure the react component's props match the arguments to the action creator.
type ActionCreatorToDispatch<A, Creators> = {
  [P in keyof Creators]: Creators[P] extends (args: infer Args) => A
    ? {} extends Args
      ? () => void
      : (args: Args) => void
    : never;
};
interface Connect<ActionBeforeMiddleware, State> {
  <P, TOwnProps = {}>(getProps: (s: State) => P): reactRedux.InferableComponentEnhancerWithProps<P, TOwnProps>;

  <P, TOwnProps = {}>(getProps: (s: State, ownProps: TOwnProps) => P): reactRedux.InferableComponentEnhancerWithProps<
    P,
    TOwnProps
  >;

  <P, Creators, TOwnProps = {}>(
    getProps: (s: State) => P,
    actionCreators: Creators
  ): reactRedux.InferableComponentEnhancerWithProps<
    P & ActionCreatorToDispatch<ActionBeforeMiddleware, Creators>,
    TOwnProps
  >;
}

// extract the type and payload of a union of action types.
// (The union of action types is produced by ReducerFnsToActions above.)
type RemoveTypeProp<P> = P extends "type" ? never : P;
type RemoveType<A> = { [P in RemoveTypeProp<keyof A>]: A[P] };
type ActionTypes<A> = A extends { type: infer T } ? T : never;
type Payload<A, T> = A extends { type: T } ? RemoveType<A> : never;

// a generic action creator factory which takes the payload (object without the type property)
// and adds the type literal to the object.
export interface ActionCreatorFactory<AppActionBeforeMiddleware> {
  <T extends ActionTypes<AppActionBeforeMiddleware>>(ty: T): (
    payload: Payload<AppActionBeforeMiddleware, T>
  ) => AppActionBeforeMiddleware;
}
export function mkACF<AppActionBeforeMiddleware>(): ActionCreatorFactory<AppActionBeforeMiddleware> {
  // any is needed for payload since typescript can't guarantee that ActionPayload<A, T> is
  // an object.  ActionPayload<A, T> will be an object exactly when the action type T appears
  // exactly once in the action sum type, which should always be the case.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return (ty) => (payload: any) => {
    if (payload) {
      return { ...payload, type: ty };
    } else {
      return { type: ty };
    }
  };
}
// a helper type to pull out the payload for an action creator built using the action creator factory.
export type ACPayload<S, T> = S extends Store<infer Act, infer State> ? Payload<Act, T> : never;

// The react-redux store with the state and action types (which will be infered from the reducers).
export interface Store<ActionBeforeMiddleware, State> {
  readonly Provider: React.ComponentType<{ children: React.ReactNode }>;
  readonly connect: Connect<ActionBeforeMiddleware, State>;
  readonly mkAC: ActionCreatorFactory<ActionBeforeMiddleware>;
  dispatch(a: ActionBeforeMiddleware): void;
  getState(): Readonly<State>;
  subscribe(listener: () => void): redux.Unsubscribe;
}
export type StoreActions<S> = S extends Store<infer Act, infer State> ? Act : never;
export type StoreState<S> = S extends Store<infer Act, infer State> ? State : never;

export function createStore<Reducers, ActionBeforeMiddleware>(
  reducers: Reducers,
  middleware: Middleware<ActionBeforeMiddleware, ReducerFnsToActions<Reducers>>,
  middlewareToEnhancer?: (m: redux.Middleware) => redux.StoreEnhancer
): Store<ActionBeforeMiddleware, ReducerFnsToState<Reducers>> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const reduxMiddleware = (_store: any) => middleware as any;
  const st = redux.createStore(
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    redux.combineReducers(reducers as any),
    (middlewareToEnhancer || redux.applyMiddleware)(reduxMiddleware)
  );
  return {
    connect: reactRedux.connect,
    mkAC: mkACF(),
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    dispatch: st.dispatch as any,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    getState: st.getState.bind(st) as any,
    subscribe: st.subscribe.bind(st),
    Provider: function StoreProvider({ children }: { children: React.ReactNode }) {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      return React.createElement(reactRedux.Provider, { store: st as any }, children);
    },
  };
}
